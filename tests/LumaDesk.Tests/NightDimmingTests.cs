// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.Text.Json;

internal static class NightDimmingTests
{
    internal static void Run(Action<bool, string> check)
    {
        var day = new DateTime(2026, 11, 15);
        var state = new NightDimming();
        var defaults = JsonSerializer.Deserialize<Preferences>("{}", Storage.Json)!;
        check(!defaults.ExperimentalNightDimming, "Existing settings and new installs default night dimming OFF");
        check(!state.Update(defaults.ExperimentalNightDimming, defaults.NightDimmingStart, defaults.NightDimmingEnd, day.AddHours(22)) &&
            !state.IsActive && state.EffectiveDimming(0) == 0, "Disabled night window never adds shading");

        bool Update(DateTime now, string? start = "21:00", string? end = "07:00", bool enabled = true) =>
            state.Update(enabled, start, end, now);
        check(!Update(day.AddHours(21).AddTicks(-1)) && !state.IsActive, "Night window excludes instant before start");
        check(Update(day.AddHours(21)) && state.IsActive, "Night window includes exact start");
        check(!Update(day.AddDays(1)) && state.IsActive, "Overnight window continues across midnight");
        check(!Update(day.AddDays(1).AddHours(7).AddTicks(-1)) && state.IsActive, "Overnight window includes instant before end");
        check(Update(day.AddDays(1).AddHours(7)) && !state.IsActive, "Night window excludes exact end");
        check(Update(day.AddHours(22)), "Startup or resume inside a window activates without crossing its start");
        check(state.EffectiveDimming(0) == 35 && state.EffectiveDimming(20) == 35 && state.EffectiveDimming(60) == 60,
            "Night shading applies a 35 percent floor and preserves stronger manual dimming");
        Update(day.AddDays(1).AddHours(12));
        check(state.EffectiveDimming(20) == 20, "Missed end after sleep releases only temporary shading");

        foreach (var (start, end, from, to) in new[] { ("21:00", "07:00", 1260, 420), ("09:30", "17:45", 570, 1065), ("00:00", "00:01", 0, 1), ("23:59", "00:00", 1439, 0) })
        {
            bool correct = true;
            for (int minute = 0; minute < 1440; minute++)
            {
                Update(day.AddMinutes(minute), start, end);
                bool expected = from < to ? minute >= from && minute < to : minute >= from || minute < to;
                correct &= state.IsActive == expected;
            }
            check(correct, "Every minute has correct local-clock membership: " + start + "–" + end);
        }
        foreach (string? invalid in new[] { null, "", "24:00", "21:60", "9:00", "21:00:00", "-1:00", " 21:00" })
        {
            Update(day.AddHours(22));
            check(Update(day.AddHours(22), invalid) && !state.IsActive, "Invalid start releases shading: " + invalid);
            Update(day.AddHours(22));
            check(Update(day.AddHours(22), end: invalid) && !state.IsActive, "Invalid end releases shading: " + invalid);
        }
        check(!NightDimming.ValidWindow("21:00", "21:00"), "Equal endpoints cannot accidentally enable all-day dimming");
        Update(day.AddHours(22));
        check(Update(day.AddHours(22), "21:00", "21:00") && !state.IsActive, "Equal endpoints release an active window");

        Update(day.AddHours(22)); state.Suspend();
        check(!state.IsActive && state.EffectiveDimming(0) == 0, "Reset immediately suspends this night");
        check(!Update(day.AddHours(23)) && !Update(day.AddDays(1).AddHours(6)) && !state.IsActive,
            "Suspension survives repeated ticks and midnight");
        check(Update(day.AddDays(1).AddHours(21)) && state.IsActive, "Next daily occurrence resumes normally");
        state.Suspend();
        check(Update(day.AddDays(1).AddHours(22), "20:00", "06:00") && state.IsActive, "Editing the schedule creates a new occurrence");
        state.Suspend();
        Update(day.AddDays(1).AddHours(22), enabled: false);
        check(Update(day.AddDays(1).AddHours(22)) && state.IsActive, "Explicit disable and re-enable clears suspension");
        state.Suspend();
        Update(day.AddDays(1).AddHours(23));
        check(!Update(day.AddDays(1).AddHours(22)) && !state.IsActive, "Backward clock movement within suspended window does not reactivate it");
        check(!Update(day.AddDays(2).AddHours(12)) && !state.IsActive, "Clock jump past the window does not replay missed dimming");

        var saved = new Preferences { ExperimentalNightDimming = true, NightDimmingStart = "22:15", NightDimmingEnd = "06:30" };
        var roundtrip = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(saved, Storage.Json), Storage.Json)!;
        check(roundtrip.ExperimentalNightDimming && roundtrip.NightDimmingStart == "22:15" && roundtrip.NightDimmingEnd == "06:30",
            "Night window opt-in and exact times roundtrip through settings");
    }
}
