// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestNextScheduleFixture(MainWindow owner)
    {
        var previousDisplays = owner.Displays;
        var previousPresets = owner.Preferences.Presets;
        string? previousSelection = owner.Preferences.QuickSelectedDisplayId;
        string? previousMore = owner.Preferences.QuickMoreDisplayId;
        owner.Preferences.DisplayOptions.TryGetValue("schedule-fixture", out var previousOptions);
        var previousCulture = CultureInfo.CurrentCulture;
        var preset = new Preset
        {
            Name = "Evening", AtTime = "21:00",
            Displays = new() { new() { Id = "schedule-fixture" } }
        };
        var now = new DateTime(2026, 11, 15, 12, 0, 0);
        QuickWindow? quick = null;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            owner.Displays = new() { new() { Id = "schedule-fixture", Name = "Fixture", Number = 1, Brightness = 50 } };
            owner.Preferences.Presets = new() { preset };
            owner.Preferences.QuickSelectedDisplayId = "schedule-fixture";
            owner.Preferences.QuickMoreDisplayId = null;
            string original = JsonSerializer.Serialize(preset, Storage.Json);
            quick = new QuickWindow(owner, dismissOnDeactivate: false);
            quick.Render();
            // Build the detached visual tree without loading a window or querying hardware.
            var content = (FrameworkElement)quick.Content;
            content.Measure(new Size(360, 620));
            content.Arrange(new Rect(0, 0, 360, 620));
            var status = Descendants<TextBlock>(content).Single(t => AutomationProperties.GetAutomationId(t) == "quick-next-schedule");
            var slider = Descendants<Slider>(content).First();
            Check(status.Visibility == Visibility.Visible && status.Text.Contains(preset.Name),
                "Rendering the tray populates its upcoming schedule without waiting for a clock tick");
            quick.RefreshScheduleStatus(now);
            Check(status.Visibility == Visibility.Visible && status.Text == L.F("automation.next", preset.Name, now.Date.AddHours(21).ToString("g")) &&
                AutomationProperties.GetName(status) == status.Text && (string)status.ToolTip == L.T("automation.nextHint"),
                "Tray schedule shows a localized, accessible preset name and regional date/time");
            quick.RefreshScheduleStatus(now.Date.AddHours(21));
            Check(status.Text == L.F("automation.next", preset.Name, now.Date.AddDays(1).AddHours(21).ToString("g")) &&
                ReferenceEquals(slider, Descendants<Slider>(content).First()),
                "Clock refresh advances the date without rebuilding brightness controls");
            Check(JsonSerializer.Serialize(preset, Storage.Json) == original && owner.Displays[0].Brightness == 50,
                "Displaying the schedule leaves the preset and display brightness unchanged");
            preset.AtTime = null; quick.RefreshScheduleStatus(now);
            Check(status.Visibility == Visibility.Collapsed && status.Text == "", "Disabling the last schedule removes its tray status");
            preset.AtTime = "invalid"; quick.RefreshScheduleStatus(now);
            Check(status.Visibility == Visibility.Collapsed, "Invalid schedule times are hidden in the tray");
            preset.AtTime = "22:15"; quick.RefreshScheduleStatus(now);
            Check(status.Visibility == Visibility.Visible && status.Text.Contains(now.Date.AddHours(22).AddMinutes(15).ToString("g")),
                "Editing a schedule updates the existing tray status");
            owner.Displays.Clear(); quick.RefreshScheduleStatus(now);
            Check(status.Visibility == Visibility.Collapsed, "Disconnecting a required display hides the upcoming schedule");
            owner.Displays.Add(new() { Id = "schedule-fixture" }); quick.RefreshScheduleStatus(now);
            Check(status.Visibility == Visibility.Visible, "Reconnecting the matching display restores the schedule status");
            owner.Preferences.Presets.Clear(); quick.RefreshScheduleStatus(now);
            Check(status.Visibility == Visibility.Collapsed, "Deleting the last preset clears the tray status");
        }
        finally
        {
            quick?.Close();
            owner.Displays = previousDisplays;
            owner.Preferences.Presets = previousPresets;
            owner.Preferences.QuickSelectedDisplayId = previousSelection;
            owner.Preferences.QuickMoreDisplayId = previousMore;
            if (previousOptions == null) owner.Preferences.DisplayOptions.Remove("schedule-fixture");
            else owner.Preferences.DisplayOptions["schedule-fixture"] = previousOptions;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
