// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class BrightnessTransitionTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (string? value in new string?[] { null, "", "0", "true", "yes", " 1", "2" })
            check(!BrightnessTransition.IsEnabled(value), "Brightness transitions require explicit opt-in: " + value);
        check(BrightnessTransition.IsEnabled("1"), "Brightness transition flag accepts 1");

        var writes = new List<CombinedLevel>();
        var waits = new List<TimeSpan>();
        TimeSpan now = TimeSpan.Zero;
        Task Delay(TimeSpan duration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            waits.Add(duration); now += duration; return Task.CompletedTask;
        }
        Task Write(CombinedLevel level) { writes.Add(level); return Task.CompletedTask; }
        void Reset() { now = TimeSpan.Zero; writes.Clear(); waits.Clear(); }
        Task Apply(CombinedLevel start, CombinedLevel target, bool enabled = true) =>
            BrightnessTransition.ApplyAsync(start, target, enabled, Write, elapsed: () => now, delay: Delay);

        await Apply(new(0, 0), new(100, 0), enabled: false);
        check(writes.SequenceEqual(new[] { new CombinedLevel(100, 0) }) && waits.Count == 0,
            "Default-off path writes the exact target once without a delay");
        Reset();
        await Apply(new(40, 12), new(40, 12));
        check(writes.SequenceEqual(new[] { new CombinedLevel(40, 12) }) && waits.Count == 0,
            "Unchanged target retains normal completion without a transition delay");

        Reset();
        await Apply(new(0, 0), new(100, 0));
        check(writes.Select(w => w.Hardware).SequenceEqual(new int?[] { 16, 50, 84, 100 }) &&
            waits.All(w => w == TimeSpan.FromMilliseconds(60)) && now == TimeSpan.FromMilliseconds(240),
            "Backlight eases in and out over four samples in 240 ms");

        foreach (var pair in new[]
        {
            (new CombinedLevel(100, 0), new CombinedLevel(0, 85)),
            (new CombinedLevel(0, 85), new CombinedLevel(100, 0)),
            (new CombinedLevel(null, 0), new CombinedLevel(null, 85)),
            (new CombinedLevel(null, 85), new CombinedLevel(null, 0)),
            (new CombinedLevel(0, 0), new CombinedLevel(0, 63.75)),
        })
        {
            Reset(); await Apply(pair.Item1, pair.Item2);
            var values = new[] { pair.Item1 }.Concat(writes).Select(w => BrightnessRules.Combine(w.Hardware, w.Dimming)).ToArray();
            bool increasing = values[^1] >= values[0];
            check(values.Zip(values.Skip(1)).All(p => increasing ? p.Second >= p.First : p.Second <= p.First) &&
                writes.All(w => w.Dimming is >= 0 and <= 85 && w.Hardware.HasValue == pair.Item1.Hardware.HasValue) &&
                writes[^1] == pair.Item2 && writes.Count <= 4,
                "Hardware/software transition is monotonic, bounded and reaches the exact pair: " + pair);
        }

        Reset(); await Apply(new(0, 0), new(1, 0));
        check(writes.SequenceEqual(new[] { new CombinedLevel(1, 0) }),
            "Rounded duplicate frames do not repeat a hardware write");

        Reset();
        await BrightnessTransition.ApplyAsync(new(0, 0), new(100, 0), true, level =>
        {
            writes.Add(level); now += TimeSpan.FromMilliseconds(300); return Task.CompletedTask;
        }, elapsed: () => now, delay: Delay);
        check(writes.Select(w => w.Hardware).SequenceEqual(new int?[] { 16, 100 }) && waits.Count == 1,
            "Slow hardware skips missed frames and finishes at the exact target without extra waits");

        Reset();
        using (var cancel = new CancellationTokenSource())
        {
            bool cancelled = false;
            try
            {
                await BrightnessTransition.ApplyAsync(new(null, 0), new(null, 85), true, Write, cancel.Token,
                    () => now, (duration, _) => { now += duration; cancel.Cancel(); return Task.CompletedTask; });
            }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && writes.Count == 0, "Shutdown during a delay cancels before touching an overlay");
        }

        Reset();
        using (var cancel = new CancellationTokenSource())
        {
            bool cancelled = false;
            try
            {
                await BrightnessTransition.ApplyAsync(new(0, 0), new(100, 0), true, level =>
                {
                    writes.Add(level); cancel.Cancel(); return Task.CompletedTask;
                }, cancel.Token, () => now, Delay);
            }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && writes.Count == 1, "Cancellation after an in-flight write prevents remaining frames");
        }

        Reset();
        var failure = new InvalidOperationException("Disconnected fixture display");
        Exception? caught = null;
        try
        {
            await BrightnessTransition.ApplyAsync(new(0, 0), new(100, 0), true, level =>
            {
                writes.Add(level); throw failure;
            }, elapsed: () => now, delay: Delay);
        }
        catch (InvalidOperationException ex) { caught = ex; }
        check(ReferenceEquals(caught, failure) && writes.Count == 1,
            "Failed hardware write stops the transition and preserves the original error");

        Reset();
        var release = new TaskCompletionSource();
        var targets = new List<int>();
        var current = new CombinedLevel(0, 0);
        int active = 0, maxActive = 0;
        var writer = new LatestValueWriter(async target =>
        {
            targets.Add(target); maxActive = Math.Max(maxActive, ++active);
            try
            {
                now = TimeSpan.Zero;
                await BrightnessTransition.ApplyAsync(current, new(target, 0), true, async level =>
                {
                    writes.Add(level);
                    if (writes.Count == 1) await release.Task;
                    current = level;
                }, elapsed: () => now, delay: Delay);
            }
            finally { active--; }
        }, (_, ex) => { if (ex != null) throw ex; }, () => Task.CompletedTask);
        writer.Request(100); writer.Request(50); writer.Request(20);
        release.SetResult(); await writer.WhenIdle;
        check(targets.SequenceEqual(new[] { 100, 20 }) && current == new CombinedLevel(20, 0) && maxActive == 1,
            "Rapid slider changes coalesce while easing and finish at the newest target without concurrent writes");
    }
}
