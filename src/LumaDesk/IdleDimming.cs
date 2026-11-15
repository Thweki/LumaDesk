// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;

namespace LumaDesk;

// Temporary presentation state only: never write this level into display models or presets.
internal sealed class IdleDimming
{
    private long? armedAt, lastPoll;
    private int delayMinutes;
    internal bool IsDimmed { get; private set; }

    internal bool Update(bool enabled, int minutes, uint? idleMilliseconds, long now)
    {
        bool previous = IsDimmed;
        // Invalid settings/input fail open. A long polling gap (sleep or a stalled UI)
        // starts a fresh grace period instead of immediately shading on resume.
        if (!enabled || minutes is < 1 or > 60 || idleMilliseconds == null)
            Reset();
        else
        {
            if (armedAt == null || minutes != delayMinutes || now < lastPoll || now - lastPoll > 5000)
                armedAt = now;
            delayMinutes = minutes;
            lastPoll = now;
            long delay = minutes * 60_000L;
            IsDimmed = now - armedAt >= delay && idleMilliseconds >= delay;
        }
        return previous != IsDimmed;
    }

    internal void Reset()
    {
        armedAt = lastPoll = null;
        IsDimmed = false;
    }

    internal double EffectiveDimming(double baseline) => IsDimmed ? Math.Max(baseline, 60) : baseline;

    internal static uint? IdleMilliseconds(uint now, uint lastInput)
    {
        uint elapsed = unchecked(now - lastInput);
        // Handle DWORD rollover, but reject timestamps apparently in the future.
        return elapsed <= int.MaxValue ? elapsed : null;
    }
}
