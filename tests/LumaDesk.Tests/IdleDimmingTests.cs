// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.Text.Json;

internal static class IdleDimmingTests
{
    internal static void Run(Action<bool, string> check)
    {
        var defaults = JsonSerializer.Deserialize<Preferences>("{}")!;
        check(!defaults.ExperimentalIdleDimming && defaults.IdleDimmingMinutes == 5,
            "Old settings leave experimental idle dimming off with a five-minute delay");
        defaults.ExperimentalIdleDimming = true; defaults.IdleDimmingMinutes = 12;
        var saved = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(defaults))!;
        check(saved.ExperimentalIdleDimming && saved.IdleDimmingMinutes == 12, "Idle dimming opt-in and delay roundtrip");

        var dim = new IdleDimming();
        long now = 0;
        void PollFor(int seconds, bool enabled = true, int minutes = 1, uint idle = 600_000)
        {
            for (int i = 0; i < seconds * 2; i++) { now += 500; dim.Update(enabled, minutes, idle, now); }
        }
        PollFor(120, enabled: false);
        check(!dim.IsDimmed && dim.EffectiveDimming(23) == 23, "Disabled experiment never changes shading even when idle");
        dim.Update(true, 1, 600_000, now);
        PollFor(59);
        check(!dim.IsDimmed, "Enabling while already idle grants a full grace period");
        PollFor(1);
        check(dim.IsDimmed, "Continuous inactivity activates at the configured threshold");
        check(dim.EffectiveDimming(0) == 60 && dim.EffectiveDimming(25) == 60 && dim.EffectiveDimming(80) == 80,
            "Idle shade respects deeper existing dimming without stacking opacity");
        check(!dim.Update(true, 1, 600_000, now + 500), "Repeated idle polls do not reapply shades");
        now += 1000;
        check(dim.Update(true, 1, 0, now) && !dim.IsDimmed && dim.EffectiveDimming(25) == 25,
            "Keyboard or mouse activity restores the exact baseline on the next poll");
        PollFor(90, idle: 1000);
        check(!dim.IsDimmed, "Ongoing input prevents dimming beyond the grace period");
        PollFor(1);
        check(dim.IsDimmed, "A later idle period can dim again");
        check(dim.Update(false, 1, 600_000, now) && !dim.IsDimmed, "Disabling restores immediately");

        dim.Update(true, 1, 600_000, now); PollFor(60);
        dim.Reset(); PollFor(59);
        check(!dim.IsDimmed, "Manual brightness/reset clears temporary shade and rearms the delay");
        PollFor(2);
        check(dim.IsDimmed, "Manual override can dim again after a full delay");
        check(dim.Update(true, 1, null, now) && !dim.IsDimmed, "Failed input query removes temporary dimming");
        PollFor(61);
        check(dim.IsDimmed, "Input query recovery waits a fresh grace period");
        now += 60_000;
        check(dim.Update(true, 1, 600_000, now) && !dim.IsDimmed, "Sleep or long dispatcher gap restores and rearms");
        PollFor(60);
        check(dim.IsDimmed, "Dimming resumes after the post-wake grace period");
        check(dim.Update(true, 2, 600_000, now) && !dim.IsDimmed, "Changing delay clears the current shade");
        PollFor(119, minutes: 2);
        check(!dim.IsDimmed, "Updated delay does not activate early");
        PollFor(1, minutes: 2);
        check(dim.IsDimmed, "Updated two-minute delay takes effect");
        now -= 10_000;
        check(dim.Update(true, 2, 600_000, now) && !dim.IsDimmed, "Backward clock sample fails open and rearms");

        foreach (int invalid in new[] { 0, -1, 61, int.MaxValue })
        {
            PollFor(61);
            check(dim.Update(true, invalid, 600_000, now) && !dim.IsDimmed, "Invalid idle delay fails open: " + invalid);
        }
        check(IdleDimming.IdleMilliseconds(1000, 250) == 750, "Native input age uses elapsed milliseconds");
        check(IdleDimming.IdleMilliseconds(200, uint.MaxValue - 299) == 500, "Native DWORD rollover preserves elapsed time");
        check(IdleDimming.IdleMilliseconds(100, 101) == null, "Future native input timestamp fails open");
    }
}
