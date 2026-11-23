// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class WeeklyScheduleTests
{
    internal static void Run(Action<bool, string> check)
    {
        var monday = new DateTime(2026, 11, 2, 9, 0, 0);
        var scene = new Preset { Name = "Work", AtTime = "09:00" };
        bool Due(DateTime now) => AutomationRules.CrossedSchedule(scene, now.AddSeconds(-20), now);
        check(!scene.ExperimentalWeeklySchedule && Due(monday.AddDays(5)),
            "Weekly scheduling defaults off and preserves daily weekend triggers");
        scene.ExperimentalWeeklySchedule = true;
        for (int day = 0; day < 7; day++)
            check(Due(monday.AddDays(day)) == (day < 5), "Opt-in workweek schedule selects day " + day);
        check(!AutomationRules.CrossedSchedule(scene, monday, monday.AddSeconds(20)) &&
            !Due(monday.AddSeconds(-1)), "Weekly schedule fires at the boundary, not before or on the next tick");
        check(!AutomationRules.CrossedSchedule(scene, monday.AddSeconds(20), monday),
            "Weekly schedule ignores backward clock movement");

        scene.ScheduleDays = new() { DayOfWeek.Saturday, DayOfWeek.Sunday };
        check(Due(monday.AddDays(5)) && Due(monday.AddDays(6)) && !Due(monday),
            "Weekend presets can run independently of workweek presets");
        scene.ScheduleDays = new();
        check(!Due(monday), "Selecting no days suspends the time trigger");
        scene.ScheduleDays = null;
        check(!Due(monday), "Null weekday configuration does not broaden to daily scheduling");
        scene.ScheduleDays = new() { (DayOfWeek)(-1), (DayOfWeek)7 };
        check(!Due(monday), "Out-of-range weekdays cannot trigger a schedule");
        scene.ScheduleDays = new() { DayOfWeek.Monday, DayOfWeek.Monday };
        check(Due(monday), "Duplicate weekday values still yield a single due event");
        foreach (string? time in new[] { null, "", "9:00", "24:00", "09:60", "invalid" })
        {
            scene.AtTime = time;
            check(!Due(monday), "Disabled or malformed schedule time cannot run: " + time);
        }

        scene.AtTime = "23:59";
        scene.ScheduleDays = new() { DayOfWeek.Friday };
        var saturday = monday.Date.AddDays(5);
        check(AutomationRules.CrossedSchedule(scene, saturday.AddMinutes(-2), saturday.AddSeconds(10)),
            "Midnight polling checks the occurrence's Friday, not Saturday's weekday");
        scene.ScheduleDays = new() { DayOfWeek.Saturday };
        check(!AutomationRules.CrossedSchedule(scene, saturday.AddMinutes(-2), saturday.AddSeconds(10)),
            "Saturday selection cannot retroactively run Friday's event");

        scene.AtTime = "09:00";
        scene.ScheduleDays = new() { DayOfWeek.Monday };
        check(AutomationRules.CrossedSchedule(scene, monday.AddDays(-3), monday.AddHours(1)),
            "Resume can catch up the latest selected occurrence within one day");
        check(!AutomationRules.CrossedSchedule(scene, monday.AddDays(-3), monday.AddDays(1).AddHours(1)),
            "Resume does not search back to an older selected weekday");
        check(AutomationRules.CrossedSchedule(scene, monday.AddDays(-14), monday),
            "Long sleep produces one current occurrence, without a backlog");
        scene.AtTime = "00:00";
        check(Due(monday.Date), "Selected midnight schedule handles the start of the week");

        var legacy = JsonSerializer.Deserialize<Preset>("{\"Name\":\"Daily\",\"AtTime\":\"09:00\"}", Storage.Json)!;
        check(!legacy.ExperimentalWeeklySchedule && AutomationRules.CrossedSchedule(legacy, saturday.AddHours(8), saturday.AddHours(9)),
            "Existing settings remain daily without migrating or opting users in");
        scene.AtTime = "09:00";
        var restored = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(scene, Storage.Json), Storage.Json)!;
        check(restored.ExperimentalWeeklySchedule && restored.ScheduleDays!.SequenceEqual(scene.ScheduleDays) &&
            AutomationRules.CrossedSchedule(restored, monday.AddSeconds(-20), monday) &&
            !AutomationRules.CrossedSchedule(restored, saturday.AddHours(8), saturday.AddHours(9)),
            "The opt-in flag and selected days survive settings serialization and retain behavior");
        var copy = SceneService.Duplicate(scene, "Copy", new[] { scene });
        check(!copy.ExperimentalWeeklySchedule && copy.AtTime == null,
            "Duplicating a weekly preset leaves the copy's automation disabled");
        scene.ExperimentalWeeklySchedule = false;
        scene.ScheduleDays = new();
        check(Due(monday.AddDays(5)), "Turning the experiment off restores the original daily time trigger");
    }
}
