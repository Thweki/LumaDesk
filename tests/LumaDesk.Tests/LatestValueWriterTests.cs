// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class LatestValueWriterTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (bool returnToInFlightValue in new[] { false, true })
        {
            var writes = new List<int>();
            var release = new TaskCompletionSource();
            int delays = 0, completions = 0, idles = 0;
            var writer = new LatestValueWriter(async value =>
            {
                writes.Add(value);
                if (writes.Count == 1) await release.Task;
            }, (_, _) => completions++, () => { delays++; return Task.CompletedTask; }, () => idles++);

            writer.Request(40);
            if (returnToInFlightValue) writer.Request(70);
            writer.Request(40); writer.Request(40);
            release.SetResult(); await writer.WhenIdle;
            check(writes.SequenceEqual(new[] { 40 }) && delays == 1 && completions == 1 && idles == 1 && !writer.IsActive,
                returnToInFlightValue ? "Returning to in-flight slider value drops superseded work" : "Duplicate in-flight slider values avoid a second write and delay");

            writer.Request(40); await writer.WhenIdle;
            check(writes.SequenceEqual(new[] { 40, 40 }) && completions == 2 && idles == 2,
                "Same value after idle is applied again without caching hardware state");
        }

        {
            var writes = new List<int>();
            var release = new TaskCompletionSource();
            int failures = 0, successes = 0;
            var writer = new LatestValueWriter(async value =>
            {
                writes.Add(value);
                if (writes.Count == 1) { await release.Task; throw new InvalidOperationException("Write failed"); }
            }, (_, error) => { if (error == null) successes++; else failures++; }, () => Task.CompletedTask);
            writer.Request(40); writer.Request(40);
            release.SetResult(); await writer.WhenIdle;
            check(writes.SequenceEqual(new[] { 40, 40 }) && failures == 1 && successes == 1,
                "Failed in-flight value keeps the queued duplicate for retry");
        }

        {
            var writes = new List<int>();
            var release = new TaskCompletionSource();
            var writer = new LatestValueWriter(async value =>
            {
                writes.Add(value);
                if (writes.Count == 1) await release.Task;
            }, (_, _) => { }, () => Task.CompletedTask);
            writer.Request(40); writer.Request(40); writer.Request(70);
            release.SetResult(); await writer.WhenIdle;
            check(writes.SequenceEqual(new[] { 40, 70 }), "Newer distinct slider value is preserved after a duplicate");
        }

        {
            var writes = new List<int>();
            int completions = 0;
            LatestValueWriter writer = null!;
            writer = new LatestValueWriter(value => { writes.Add(value); return Task.CompletedTask; }, (value, _) =>
            {
                if (++completions == 1) writer.Request(value);
            }, () => Task.CompletedTask);
            writer.Request(40); await writer.WhenIdle;
            check(writes.SequenceEqual(new[] { 40, 40 }) && completions == 2,
                "Completion callback can explicitly request the same value again");
        }
    }
}
