// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Globalization;

namespace LumaDesk;

// Wall-clock presentation state, separate from saved/manual brightness. No hardware writes.
internal sealed class NightDimming
{
    private (DateTime Start, DateTime End)? occurrence, suspended;
    internal bool IsActive { get; private set; }

    internal static bool ValidWindow(string? start, string? end) =>
        Parse(start, out var from) && Parse(end, out var to) && from != to;

    private static bool Parse(string? value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out time);

    internal bool Update(bool enabled, string? start, string? end, DateTime now)
    {
        bool previous = IsActive;
        occurrence = null;
        if (!enabled || !ValidWindow(start, end)) suspended = null;
        else
        {
            Parse(start, out var from); Parse(end, out var to);
            var begin = now.Date + from;
            var finish = now.Date + to;
            if (from > to)
            {
                if (now.TimeOfDay < to) begin = begin.AddDays(-1);
                else finish = finish.AddDays(1);
            }
            if (now >= begin && now < finish) occurrence = (begin, finish);
        }
        IsActive = occurrence.HasValue && occurrence != suspended;
        return previous != IsActive;
    }

    internal void Suspend()
    {
        suspended = occurrence;
        IsActive = false;
    }

    // Keep stronger manual shading and never alter the baseline recorded in presets.
    internal double EffectiveDimming(double baseline) => IsActive ? Math.Max(baseline, 35) : baseline;
}
