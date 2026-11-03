// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Threading.Tasks;

namespace LumaDesk;

// Serializes slow hardware writes, keeping only the latest unsent slider value.
// Call on a single UI context; the callback retains that synchronization context.
public sealed class LatestValueWriter
{
    private readonly Func<int, Task> write;
    private readonly Action<int, Exception?> completed;
    private readonly Func<Task> delay;
    private readonly Action? idle;
    private int? pending;
    private bool stopped;
    public bool IsActive { get; private set; }
    public Task WhenIdle { get; private set; } = Task.CompletedTask;
    public LatestValueWriter(Func<int, Task> write, Action<int, Exception?> completed, Func<Task>? delay = null, Action? idle = null)
    {
        this.write = write; this.completed = completed; this.delay = delay ?? (() => Task.Delay(30)); this.idle = idle;
    }
    public void Request(int value)
    {
        if (stopped) return;
        pending = value;
        if (IsActive) return;
        IsActive = true; WhenIdle = DrainAsync();
    }
    public void Stop() { stopped = true; pending = null; }
    private async Task DrainAsync()
    {
        try
        {
            while (!stopped && pending.HasValue)
            {
                await delay();
                if (stopped || !pending.HasValue) break;
                int value = pending.Value; pending = null;
                Exception? failure = null;
                try { await write(value); } catch (Exception ex) { failure = ex; }
                completed(value, failure);
            }
        }
        finally { IsActive = false; if (!stopped) idle?.Invoke(); }
    }
}
