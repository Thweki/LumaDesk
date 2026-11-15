// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class RecoveryLifecycleTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        // Mode constructs synchronously; DPI constructs on a worker. Neither caller
        // receives a lease when apply (and its constructor rollback) throws.
        foreach (var (name, original, target, later, background) in new[] {
            ("Mode", 1920, 2560, 3840, false), ("DPI", 100, 125, 150, true)
        })
        foreach (string outcome in new[] { "keep", "apply failure", "readback failure" })
        foreach (bool failRestore in new[] { false, true })
        {
            if (outcome == "keep" && failRestore) continue;
            string scenario = $"{name}: {outcome}" + (outcome == "keep" ? "" : $", rollback {(failRestore ? "fails" : "succeeds")}");
            using var watchdog = new FakeWatchdog(original);
            var applyError = new InvalidOperationException("apply");
            var readbackError = new InvalidOperationException("readback");
            var restoreError = new InvalidOperationException("restore");
            ModeLease? lease = null;
            Exception? failure = null, rollbackFailure = null;
            int restores = 0;
            bool restoreFails = failRestore;
            bool restoredBeforeDisarm = false;
            ModeLease Create() => new(
                () => { watchdog.Value = target; if (outcome == "apply failure") throw applyError; },
                () => { restores++; if (restoreFails) throw restoreError; watchdog.Value = original; },
                () => { restoredBeforeDisarm = watchdog.Value == original; watchdog.Disarm(); });
            try
            {
                lease = background ? await Task.Run(Create) : Create();
                check(watchdog.Armed && lease.Pending, scenario + ": recovery stays armed during readback");
                if (outcome == "readback failure") throw readbackError;
                lease.Keep();
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (lease?.Pending == true)
                {
                    try { if (background) await Task.Run(lease.Revert); else lease.Revert(); }
                    catch (Exception ex) { rollbackFailure = ex; }
                }
            }
            check(outcome != "apply failure" || lease == null, scenario + ": failed constructor returns no lease");
            var expectedFailure = outcome == "keep" ? null : outcome == "readback failure" ? readbackError : failRestore ? restoreError : applyError;
            check(ReferenceEquals(failure, expectedFailure), scenario + ": operation reports failure");
            check(ReferenceEquals(rollbackFailure, outcome == "readback failure" && failRestore ? restoreError : null), scenario + ": caller rollback reports failure");
            check(restores == (outcome == "keep" ? 0 : 1), scenario + ": restores only when required");
            check(watchdog.Armed == failRestore && watchdog.Disarms == (failRestore ? 0 : 1), scenario + ": watchdog follows actual outcome");
            if (outcome == "keep")
                check(watchdog.Value == target && !lease!.Pending, scenario + ": committed choice is retained");
            if (outcome != "keep" && !failRestore)
                check(restoredBeforeDisarm && watchdog.Value == original, scenario + ": restore finishes before disarming");
            if (failRestore)
            {
                if (lease != null)
                {
                    check(lease.Pending, scenario + ": failed rollback remains pending");
                    restoreFails = false;
                    lease.Revert();
                    check(!lease.Pending && !watchdog.Armed && watchdog.Value == original, scenario + ": retry restores and disarms");
                }
                else
                {
                    watchdog.Expire();
                    check(watchdog.Value == original, scenario + ": retained watchdog can recover");
                }
            }
            else
            {
                lease?.Revert();
                watchdog.Value = later;
                watchdog.Expire();
                check(watchdog.Value == later && watchdog.Disarms == 1, scenario + ": stale watchdog cannot undo a later choice");
            }
        }
    }

    private sealed class FakeWatchdog : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "lumadesk-watchdog-" + Guid.NewGuid().ToString("N"));
        private readonly int original;
        public int Value { get; set; }
        public int Disarms { get; private set; }
        public bool Armed => File.Exists(path);
        public FakeWatchdog(int original) { this.original = Value = original; File.WriteAllText(path, "armed"); }
        public void Disarm() { File.Delete(path); Disarms++; }
        public void Expire() { if (Armed) { Value = original; File.Delete(path); } }
        public void Dispose() => File.Delete(path);
    }
}
