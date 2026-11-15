// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Controls;

internal static partial class Program
{
    private static void TestNightDimmingFixture(MainWindow owner)
    {
        var original = owner.Displays;
        var preferences = owner.Preferences;
        var now = new DateTime(2026, 11, 15, 22, 0, 0);
        var first = new DisplayInfo { Id = "night-a", Brightness = 50, Dimming = 0 };
        var second = new DisplayInfo { Id = "night-b", Brightness = null, Dimming = 60 };
        owner.Displays = new() { first, second };
        var writes = new List<(string Id, double Dimming)>();
        void Apply(DisplayInfo display, double dimming) => writes.Add((display.Id, dimming));
        string remembered = JsonSerializer.Serialize(preferences.RememberedBrightness, Storage.Json);
        try
        {
            owner.UpdateNightDimming(now, force: true, applyOverlay: Apply);
            Check(writes.Count == 0, "Disabled night schedule leaves the default refresh path untouched");
            preferences.ExperimentalNightDimming = true;
            owner.UpdateNightDimming(now, applyOverlay: Apply);
            Check(writes.SequenceEqual(new[] { (first.Id, 35d), (second.Id, 60d) }),
                "Night schedule shades hardware and software-only displays without reducing stronger shading");
            Check(first.Brightness == 50 && first.Dimming == 0 && second.Brightness == null && second.Dimming == 60 &&
                JsonSerializer.Serialize(preferences.RememberedBrightness, Storage.Json) == remembered,
                "Scheduled shading never changes baseline models or remembered brightness");
            writes.Clear(); owner.UpdateNightDimming(now.AddMinutes(1), applyOverlay: Apply);
            Check(writes.Count == 0, "Unchanged night window does not repeatedly recreate overlays");
            var connected = new DisplayInfo { Id = "night-c", Dimming = 10 };
            owner.Displays.Add(connected);
            owner.UpdateNightDimming(now, force: true, applyOverlay: Apply);
            Check(writes.Contains((connected.Id, 35d)), "Display refresh applies an active window to newly connected screens");
            owner.Displays.Remove(connected);
            first.Dimming = 20; // A manual or preset change during the window becomes the new baseline.
            writes.Clear(); owner.UpdateNightDimming(now.Date.AddDays(1).AddHours(7), applyOverlay: Apply);
            Check(writes.SequenceEqual(new[] { (first.Id, 20d), (second.Id, 60d) }),
                "Window end restores current manual shading and ignores disconnected displays");
            owner.UpdateNightDimming(now, applyOverlay: Apply);
            writes.Clear(); owner.SuspendNightDimming(now, Apply);
            Check(writes.Contains((first.Id, 20d)), "Reset releases scheduled shading immediately");
            writes.Clear(); owner.UpdateNightDimming(now.AddHours(1), applyOverlay: Apply);
            Check(writes.Count == 0, "Reset suspension survives subsequent automation ticks");
            owner.UpdateNightDimming(now.AddDays(1), applyOverlay: Apply);
            writes.Clear(); preferences.ExperimentalNightDimming = false;
            owner.UpdateNightDimming(now.AddDays(1), applyOverlay: Apply);
            Check(writes.Contains((first.Id, 20d)), "Disabling the feature immediately restores baseline shading");

            preferences.ExperimentalNightDimming = true;
            int attempts = 0;
            owner.UpdateNightDimming(now, applyOverlay: (display, value) =>
            {
                attempts++; if (display == first) throw new InvalidOperationException("Fixture overlay failure");
            });
            Check(attempts == 2, "One overlay failure does not prevent shading another display");
            writes.Clear(); owner.UpdateNightDimming(now.AddMinutes(1), applyOverlay: Apply);
            Check(writes.Count == 2, "Failed overlay updates retry on the next tick");
            writes.Clear(); owner.SuspendNightDimming(now.Date.AddDays(1).AddHours(7), Apply);
            Check(writes.Contains((first.Id, 20d)), "Reset at an unpolled end boundary still removes the previous overlay");

            // Detached options never start discovery or write hardware.
            preferences.ExperimentalNightDimming = false;
            var controls = owner.CreateNightDimmingOptions();
            var toggle = Descendants<CheckBox>(controls).Single();
            Check(toggle.IsChecked == false && AutomationProperties.GetAutomationId(toggle) == "night-dimming-enabled",
                "Application settings expose an explicitly disabled experimental toggle");
            Check(Descendants<TextBox>(controls).Select(t => t.Text).SequenceEqual(new[] { "21:00", "07:00" }),
                "Night settings expose both editable 24-hour endpoints");
        }
        finally
        {
            preferences.ExperimentalNightDimming = false;
            owner.UpdateNightDimming(now, applyOverlay: Apply);
            owner.Displays = original;
        }
    }
}
