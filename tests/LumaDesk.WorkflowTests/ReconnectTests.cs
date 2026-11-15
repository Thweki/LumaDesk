// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class ReconnectTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await RetryAsync(check);
        await PartialFailureAsync(check);
        await ManualDuringBackoffAsync(check);
        await ManualDuringScanAsync(check, 1);
        await ManualDuringScanAsync(check, 2);
        await ManualDuringWriteAsync(check, cancel: true);
        await ManualDuringWriteAsync(check, cancel: false);
        await ManualAfterSuccessAsync(check);
        await OwnershipAndCleanupAsync(check);
        await SelectionAsync(check);
    }

    private static Task Finish(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task RetryAsync(Action<bool, string> check)
    {
        var backend = new FakeReconnectBackend("A");
        var delays = new List<TimeSpan>();
        var coordinator = new ReconnectCoordinator(backend, Array.Empty<string>(), duration =>
        { delays.Add(duration); return Task.CompletedTask; });
        backend.Write = (_, _, _) => backend.Writes.Count < 3
            ? Task.FromException(new InvalidOperationException("DDC not ready")) : Task.CompletedTask;
        // Fresh scan objects even when the driver varies the casing of its ID.
        backend.Refresh = () => { backend.Screens[0].Id = "a"; return Task.CompletedTask; };
        await Finish(coordinator.RestoreAsync(false));
        check(backend.Writes.Count == 3 && backend.Refreshes == 3 && delays.SequenceEqual(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }),
            "Reconnect retries DDC readiness at most three times with 1s/2s backoff");
        check(backend.Writes.Select(w => w.Display).Distinct().Count() == 3 && backend.StaleWrites == 0 && backend.Writes.All(w => w.Value == 65 && w.Token.CanBeCanceled) && backend.Writes.Select(w => w.Token).Distinct().Count() == 1,
            "Reconnect reacquires display objects while retaining the same write ownership across retries");
        check(backend.Screens[0].Brightness == 65 && backend.Dims.Single().Value == 12 && backend.Failures.Count == 0 && backend.Completions.Single().AllowScene,
            "Successful retry restores brightness and dimming and permits the connection scene");
        check(!coordinator.IsRestoring, "Reconnect releases ownership after success");
    }

    private static async Task PartialFailureAsync(Action<bool, string> check)
    {
        var backend = new FakeReconnectBackend("A", "B");
        var coordinator = new ReconnectCoordinator(backend, Array.Empty<string>(), _ => Task.CompletedTask);
        backend.Write = (display, _, _) => display.Id == "A"
            ? Task.FromException(new InvalidOperationException("Still unavailable")) : Task.CompletedTask;
        await Finish(coordinator.RestoreAsync(false));
        check(backend.Writes.Count(w => w.Display.Id == "A") == 3 && backend.Writes.Count(w => w.Display.Id == "B") == 1 && backend.Dims.Single().Id == "B",
            "Reconnect retries only failed displays and preserves another display's successful restoration");
        check(backend.Failures.Single().Single().Id == "A" && backend.Errors == 3 && !coordinator.IsRestoring,
            "Retry exhaustion reports only the remaining target and releases ownership");

        backend = new FakeReconnectBackend("A");
        coordinator = new(backend, Array.Empty<string>(), _ => Task.CompletedTask);
        backend.Refresh = () =>
        {
            if (backend.Refreshes == 1) backend.Screens[0].Brightness = null;
            if (backend.Refreshes == 2) backend.Screens.Clear();
            return Task.CompletedTask;
        };
        await Finish(coordinator.RestoreAsync(false));
        check(backend.Refreshes == 3 && backend.Writes.Count == 1 && backend.Dims.Count == 1 && backend.Failures.Count == 0,
            "Reconnect retains a target through unsupported DDC and a temporarily missing display");
    }

    private static async Task ManualDuringBackoffAsync(Action<bool, string> check)
    {
        var backend = new FakeReconnectBackend("A", "B");
        var gate = new TaskCompletionSource();
        var coordinator = new ReconnectCoordinator(backend, Array.Empty<string>(), _ => gate.Task);
        backend.Write = (_, _, _) => backend.Refreshes == 1
            ? Task.FromException(new InvalidOperationException("Wake pending")) : Task.CompletedTask;
        var run = coordinator.RestoreAsync(false);
        var token = backend.Writes[0].Token;
        coordinator.Cancel("a");
        check(coordinator.IsRestoring && token.IsCancellationRequested, "Manual override cancels the owned token during retry backoff, ignoring ID casing");
        gate.SetResult();
        await Finish(run);
        check(backend.Writes.Count(w => w.Display.Id == "A") == 1 && backend.Writes.Count(w => w.Display.Id == "B") == 2 && backend.Dims.Single().Id == "B",
            "Manual override survives refresh and prevents retries only for the adjusted display");
        check(backend.Failures.Count == 0 && !backend.Completions.Single().AllowScene,
            "Cancelled restoration is not reported as failure and cannot trigger a connection scene over manual input");
    }

    private static async Task ManualDuringScanAsync(Action<bool, string> check, int scan)
    {
        var backend = new FakeReconnectBackend("A");
        var gate = new TaskCompletionSource();
        var coordinator = new ReconnectCoordinator(backend, Array.Empty<string>(), _ => Task.CompletedTask);
        backend.Refresh = () => backend.Refreshes == scan ? gate.Task : Task.CompletedTask;
        backend.Write = (_, _, _) => Task.FromException(new InvalidOperationException("Not ready"));
        var run = coordinator.RestoreAsync(false);
        check(coordinator.IsRestoring && backend.Refreshes == scan, $"Reconnect owns restoration while scan {scan} is suspended");
        // Cancellation alone must work, even before the manual hardware write has
        // completed and RememberedBrightness has been updated.
        coordinator.Cancel("A");
        gate.SetResult();
        await Finish(run);
        check(backend.Writes.Count == scan - 1 && backend.Dims.Count == 0 && backend.Failures.Count == 0 && !backend.Completions.Single().AllowScene,
            $"Manual adjustment during scan {scan} survives target creation and prevents saved brightness, dimming and scene restoration");
    }

    private static async Task ManualDuringWriteAsync(Action<bool, string> check, bool cancel)
    {
        var backend = new FakeReconnectBackend("A");
        var gate = new TaskCompletionSource();
        var coordinator = new ReconnectCoordinator(backend, Array.Empty<string>(), _ => Task.CompletedTask);
        // Simulate an already-issued native call which cannot be interrupted.
        backend.Write = (_, _, _) => gate.Task;
        var run = coordinator.RestoreAsync(false);
        var display = backend.Writes.Single().Display;
        if (cancel) coordinator.Cancel("A");
        display.Brightness = 19; display.Dimming = 7;
        backend.Remember(display);
        gate.SetResult();
        await Finish(run);
        check(display.Brightness == 19 && display.Dimming == 7 && backend.Preferences.RememberedBrightness["A"].Brightness == 19 && backend.Dims.Count == 0,
            $"Late reconnect completion cannot overwrite manual state ({(cancel ? "cancelled token" : "replaced remembered target")})");
        check(backend.Writes.Single().Token.IsCancellationRequested && backend.Failures.Count == 0 && !backend.Completions.Single().AllowScene,
            "In-flight manual replacement cancels ownership and suppresses automatic scene restoration");
    }

    private static async Task ManualAfterSuccessAsync(Action<bool, string> check)
    {
        var backend = new FakeReconnectBackend("A", "B");
        var gate = new TaskCompletionSource();
        var coordinator = new ReconnectCoordinator(backend, Array.Empty<string>(), _ => Task.CompletedTask);
        backend.Write = (display, _, _) => display.Id == "B" ? gate.Task : Task.CompletedTask;
        var run = coordinator.RestoreAsync(false);
        coordinator.Cancel("A");
        backend.Screens[0].Brightness = 24;
        backend.Remember(backend.Screens[0]);
        gate.SetResult();
        await Finish(run);
        check(backend.Dims.Count == 2 && backend.Screens[0].Brightness == 24 && !backend.Completions.Single().AllowScene,
            "Manual changes to an already-restored display still suppress the pending connection scene");
    }

    private static async Task OwnershipAndCleanupAsync(Action<bool, string> check)
    {
        var backend = new FakeReconnectBackend("A");
        var gate = new TaskCompletionSource();
        var coordinator = new ReconnectCoordinator(backend, Array.Empty<string>(), _ => Task.CompletedTask);
        backend.Write = (_, _, _) => gate.Task;
        var run = coordinator.RestoreAsync(false);
        await Finish(coordinator.RestoreAsync(true));
        check(coordinator.IsRestoring && backend.Refreshes == 1 && backend.Writes.Count == 1,
            "Overlapping reconnect notifications cannot replace or release the active restoration");
        coordinator.CancelAll();
        check(backend.Writes.Single().Token.IsCancellationRequested, "Stopping automation cancels the active reconnect write");
        gate.SetResult();
        await Finish(run);
        check(!coordinator.IsRestoring && backend.Dims.Count == 0 && backend.Completions.Count == 0 && backend.Failures.Count == 0,
            "Stopped reconnect cannot publish late dimming, failures or scene work");
        backend.Refresh = () => Task.FromException(new InvalidOperationException("Scan failed"));
        bool threw = false;
        try { await Finish(coordinator.RestoreAsync(true)); }
        catch (InvalidOperationException) { threw = true; }
        check(threw && !coordinator.IsRestoring, "Scan exception releases reconnect ownership");
        backend.Refresh = () => Task.CompletedTask;
        backend.Write = (_, _, _) => Task.CompletedTask;
        await Finish(coordinator.RestoreAsync(true));
        check(backend.Dims.Count == 1 && backend.Completions.Single().AllowScene && !backend.Writes.Last().Token.IsCancellationRequested,
            "A subsequent wake gets fresh ownership after cancellation and a scan exception");

        gate = new TaskCompletionSource();
        backend = new FakeReconnectBackend("A");
        coordinator = new(backend, Array.Empty<string>(), _ => gate.Task);
        backend.Write = (_, _, _) => Task.FromException(new InvalidOperationException("Retry"));
        run = coordinator.RestoreAsync(false);
        backend.IsBlocked = true;
        gate.SetResult();
        await Finish(run);
        check(backend.Refreshes == 1 && backend.Writes.Count == 1 && backend.Completions.Count == 0 && !coordinator.IsRestoring,
            "Scene or mode activity during backoff stops reconnect before another refresh or write");
    }

    private static async Task SelectionAsync(Action<bool, string> check)
    {
        var backend = new FakeReconnectBackend("A", "B");
        var coordinator = new ReconnectCoordinator(backend, new[] { "a" }, _ => Task.CompletedTask);
        backend.Preferences.RememberedBrightness["B"].Brightness = null;
        await Finish(coordinator.RestoreAsync(false));
        check(backend.Writes.Count == 0 && backend.Dims.Single().Id == "B", "Hotplug restores only new IDs and supports dimming-only remembered targets");
        await Finish(coordinator.RestoreAsync(false));
        check(backend.Completions.Count == 1 && backend.Dims.Count == 1, "Unchanged topology does not replay reconnect restoration");
        await Finish(coordinator.RestoreAsync(true));
        check(backend.Dims.Count == 3 && backend.Writes.Single().Display.Id == "A" && backend.Completions.Last().Resume,
            "Wake restores remembered targets even when topology is unchanged");

        backend = new FakeReconnectBackend("A");
        backend.Preferences.RestoreBrightness = false;
        coordinator = new(backend, Array.Empty<string>(), _ => Task.CompletedTask);
        await Finish(coordinator.RestoreAsync(false));
        check(backend.Writes.Count == 0 && backend.Dims.Count == 0 && backend.Completions.Single().AllowScene,
            "Disabled brightness restoration preserves the independent connection-scene decision");
        await Finish(coordinator.RestoreAsync(true, automate: false));
        check(backend.Completions.Count == 1 && backend.Writes.Count == 0 && !coordinator.IsRestoring,
            "Integration-test enumeration cannot issue reconnect writes or connection scenes");
    }
}

internal sealed class FakeReconnectBackend : IReconnectBackend
{
    private readonly string[] ids;
    internal List<DisplayInfo> Screens = new();
    public IReadOnlyList<DisplayInfo> Displays => Screens;
    public Preferences Preferences { get; } = new();
    public bool IsBlocked { get; set; }
    internal int Refreshes, StaleWrites, Errors;
    internal readonly List<(DisplayInfo Display, int Value, CancellationToken Token)> Writes = new();
    internal readonly List<(string Id, int Value)> Dims = new();
    internal readonly List<IReadOnlyList<PresetDisplay>> Failures = new();
    internal readonly List<(bool Resume, bool AllowScene)> Completions = new();
    internal Func<Task> Refresh = () => Task.CompletedTask;
    internal Func<DisplayInfo, int, CancellationToken, Task> Write = (_, _, _) => Task.CompletedTask;

    internal FakeReconnectBackend(params string[] ids)
    {
        this.ids = ids;
        foreach (var id in ids) Preferences.RememberedBrightness[id] = new() { Id = id, Name = id, Brightness = 65, Dimming = 12 };
    }

    public async Task RefreshAsync()
    {
        Refreshes++;
        Screens = ids.Select(id => new DisplayInfo { Id = id, Name = id, Brightness = 50 }).ToList();
        await Refresh();
    }

    public Task WriteBrightnessAsync(DisplayInfo display, int value, CancellationToken cancellation)
    {
        if (!Screens.Contains(display)) StaleWrites++;
        Writes.Add((display, value, cancellation));
        return Write(display, value, cancellation);
    }

    internal void Remember(DisplayInfo display) => Preferences.RememberedBrightness[display.Id] = new()
    { Id = display.Id, Name = display.Name, Brightness = display.Brightness, Dimming = display.Dimming };

    public void SetDimming(DisplayInfo display, int value)
    {
        Dims.Add((display.Id, value)); display.Dimming = value;
        Remember(display); // Match MainWindow: successful restoration replaces the saved object.
    }

    public void LogFailure(DisplayInfo display, Exception error) => Errors++;
    public void RestoreFailed(IReadOnlyList<PresetDisplay> targets) => Failures.Add(targets);
    public Task OnRestoredAsync(bool resume, bool allowScene)
    { Completions.Add((resume, allowScene)); return Task.CompletedTask; }
}
