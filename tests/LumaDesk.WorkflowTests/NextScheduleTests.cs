// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class NextScheduleTests
{
    internal static void Run(Action<bool, string> check)
    {
        var day = new DateTime(2026, 12, 31);
        var displays = new List<DisplayInfo> { new() { Id = "A" }, new() { Id = "B" } };
        Preset Scheduled(string name, string? time) => new()
        {
            Name = name, AtTime = time,
            Displays = new() { new() { Id = "b" }, new() { Id = "a" } }
        };
        var morning = Scheduled("Work", "09:00");
        var evening = Scheduled("Evening", "21:00");
        var tied = Scheduled("Reading", "21:00");
        var presets = new List<Preset> { morning, evening, tied };
        string original = JsonSerializer.Serialize(presets, Storage.Json);
        var next = AutomationRules.NextScheduledPreset(presets, displays, day.AddHours(12));
        check(next?.Preset == evening && next?.At == day.AddHours(21),
            "Next schedule selects the earliest future time and breaks ties by preset order");
        check(AutomationRules.NextScheduledPreset(new[] { tied, evening }, displays, day.AddHours(12))?.Preset == tied,
            "Reordering tied schedules changes the displayed winner just as it changes automation");
        check(AutomationRules.NextScheduledPreset(presets, displays, day.AddHours(9).AddTicks(-1))?.Preset == morning,
            "A schedule remains upcoming immediately before its boundary");
        check(AutomationRules.NextScheduledPreset(presets, displays, day.AddHours(9))?.Preset == evening,
            "At the exact boundary, the indicator advances to the next future occurrence");
        next = AutomationRules.NextScheduledPreset(presets, displays, day.AddHours(23));
        check(next?.Preset == morning && next?.At == day.AddDays(1).AddHours(9),
            "After the last schedule, the earliest tomorrow is shown across a year boundary");
        var midnight = Scheduled("Midnight", "00:00");
        check(AutomationRules.NextScheduledPreset(new[] { midnight }, displays, day.AddHours(23))?.At == day.AddDays(1) &&
            AutomationRules.NextScheduledPreset(new[] { midnight }, displays, day)?.At == day.AddDays(1),
            "Midnight rolls over to the next day, including at exactly midnight");
        foreach (string? invalid in new string?[] { null, "", "9:00", "24:00", "21:60", "bad" })
        {
            var disabled = Scheduled("Disabled or invalid", invalid);
            disabled.OnConnect = true; disabled.OnPower = "ac";
            check(AutomationRules.NextScheduledPreset(new[] { disabled }, displays, day) == null,
                "Disabled or invalid daily schedules are ignored even with other triggers: " + invalid);
        }
        var weekly = Scheduled("Weekly", "09:00");
        weekly.ExperimentalWeeklySchedule = true;
        weekly.ScheduleDays = new() { DayOfWeek.Thursday };
        next = AutomationRules.NextScheduledPreset(new[] { weekly }, displays, day.AddHours(9));
        check(next?.At == day.AddDays(7).AddHours(9),
            "The next weekly occurrence skips unselected days and advances at the exact boundary");
        weekly.ScheduleDays.Clear();
        check(AutomationRules.NextScheduledPreset(new[] { weekly }, displays, day) == null,
            "A weekly schedule with no selected days has no upcoming occurrence");
        var missing = Scheduled("Missing display", "08:00");
        missing.Displays.Add(new() { Id = "C" });
        check(AutomationRules.NextScheduledPreset(new[] { missing, morning }, displays, day)?.Preset == morning,
            "An earlier schedule for missing displays does not hide an eligible schedule");
        check(AutomationRules.NextScheduledPreset(presets, new[] { displays[0] }, day) == null &&
            AutomationRules.NextScheduledPreset(presets, new[] { displays[0], displays[1], new DisplayInfo { Id = "C" } }, day) == null,
            "Missing and extra connected displays suppress exact-match schedules");
        check(AutomationRules.NextScheduledPreset(Array.Empty<Preset>(), displays, day) == null &&
            AutomationRules.NextScheduledPreset(new[] { new Preset { AtTime = "09:00" } }, Array.Empty<DisplayInfo>(), day) == null,
            "Empty preset lists and empty saved display sets have no next schedule");
        check(JsonSerializer.Serialize(presets, Storage.Json) == original,
            "Computing the next schedule preserves preset order, settings and automation");
    }
}
