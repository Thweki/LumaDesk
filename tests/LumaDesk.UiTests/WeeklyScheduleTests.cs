// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestWeeklyScheduleFixture(MainWindow owner)
    {
        var previous = owner.Preferences.Presets;
        var scene = new Preset { Name = "Work", OnPower = "ac", OnConnect = true };
        try
        {
            owner.Preferences.Presets = new() { scene };
            var editor = owner.CreateSceneSchedule(scene);
            CheckBox Find(string id) => Descendants<CheckBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == id);
            void Set(string id, bool value)
            {
                var toggle = Find(id);
                Check(toggle.IsEnabled, "Schedule control is available: " + id);
                toggle.IsChecked = value;
                toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            }
            Check(Find("schedule-weekly").IsChecked == false && !Find("schedule-day-Monday").IsEnabled && scene.AtTime == null,
                "Fresh preset keeps both scheduling and the weekday experiment off");
            Set("schedule-weekly", true);
            Check(!Find("schedule-day-Monday").IsEnabled && scene.AtTime == null,
                "Opting into weekdays does not silently enable the time trigger");
            Set("schedule-enabled", true);
            Check(scene.AtTime == "21:00" && Find("schedule-day-Monday").IsEnabled &&
                Find("schedule-day-Monday").IsChecked == true && Find("schedule-day-Saturday").IsChecked == false,
                "Enabling time exposes the default workweek choices");
            Set("schedule-day-Monday", false);
            Set("schedule-day-Saturday", true);
            var saved = Storage.Load().Presets.Single();
            Check(saved.ExperimentalWeeklySchedule && saved.ScheduleDays!.Contains(DayOfWeek.Saturday) &&
                !saved.ScheduleDays.Contains(DayOfWeek.Monday) && saved.AtTime == "21:00",
                "Weekday edits persist with the flag and local schedule time");
            editor = owner.CreateSceneSchedule(saved);
            Check(Find("schedule-weekly").IsChecked == true && Find("schedule-day-Saturday").IsChecked == true &&
                Find("schedule-day-Monday").IsChecked == false,
                "Reopening a saved weekly schedule displays the stored choices");
            editor = owner.CreateSceneSchedule(scene);
            Set("schedule-enabled", false);
            Check(scene.AtTime == null && !Find("schedule-day-Saturday").IsEnabled && scene.ExperimentalWeeklySchedule,
                "Disabling the time trigger retains weekday configuration without scheduling");
            Set("schedule-enabled", true);
            Set("schedule-weekly", false);
            Check(!Find("schedule-day-Saturday").IsEnabled && scene.AtTime == "21:00" &&
                Descendants<TextBlock>(editor).Any(t => t.Text == L.T("automation.daily")),
                "Disabling weekly mode restores the daily label and retains the time");
            Check(scene.OnPower == "ac" && scene.OnConnect && Storage.Load().Presets.Single().ExperimentalWeeklySchedule == false,
                "Weekday controls preserve other triggers and persist disabling the experiment");
        }
        finally
        {
            owner.Preferences.Presets = previous;
            Storage.Save(owner.Preferences);
        }
    }
}
