// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal static partial class Program
{
    private static async Task TestMultiBrightnessAsync(MainWindow window)
    {
        var real = window.Displays;
        string? oldSelected = window.Preferences.QuickSelectedDisplayId, oldMore = window.Preferences.QuickMoreDisplayId;
        bool oldSync = window.Preferences.SyncBrightness;
        var first = new DisplayInfo { Id = "multi-a", Number = 1, Name = "A", X = -2560, Current = new(2560, 1440, 60), Brightness = 50, DpiScale = DpiRules.FromRelative(0, 0, 2) };
        var second = new DisplayInfo { Id = "multi-b", Number = 2, Name = "B", Primary = true, Current = new(3840, 2160, 60), Brightness = 80, DpiScale = DpiRules.FromRelative(0, 1, 2) };
        var third = new DisplayInfo { Id = "multi-c", Number = 3, Name = "C", X = 3840, Current = new(1440, 2560, 60), Brightness = 20, DpiScale = DpiRules.FromRelative(0, 0, 2) };
        window.Displays = new() { first, second, third }; window.Preferences.SyncBrightness = true;
        window.Preferences.QuickSelectedDisplayId = second.Id; window.Preferences.QuickMoreDisplayId = second.Id;
        window.Options(first).Offset = 15; window.Options(second).Offset = -10; window.Options(third).Maximum = 70;
        var planned = window.SelectedBrightnessTargets(new[] { first.Id, third.Id, first.Id, "missing" }, 80);
        Check(planned.Count == 2 && planned[0].Display == first && planned[0].Value == 80 && planned[1].Display == third && planned[1].Value == 70,
            "Explicit brightness targets ignore global sync and relative offsets, deduplicate IDs and respect device limits");
        window.Options(third).Maximum = 100;
        int writes = 0; string[] lastIds = Array.Empty<string>(); int lastValue = 0; bool fail = false;
        TaskCompletionSource<bool>? hold = null;
        var quick = new QuickWindow(window, (_, _, _) => Task.CompletedTask, dismissOnDeactivate: false,
            applyBrightness: (_, _) => throw new Exception("Single-screen sync path must not be used"),
            applySelectedBrightness: async (ids, value) =>
            {
                writes++; lastIds = ids.ToArray(); lastValue = value;
                if (hold != null) await hold.Task;
                foreach (var target in window.SelectedBrightnessTargets(ids, value))
                {
                    if (fail && target.Display == second) continue;
                    var split = BrightnessRules.Split(target.Value, true);
                    target.Display.Brightness = split.Hardware; target.Display.Dimming = split.Dimming;
                }
                if (fail) throw new InvalidOperationException("Fixture: one selected screen rejected brightness");
            });
        Button Find(string id) => Descendants<Button>(quick).Single(b => AutomationProperties.GetAutomationId(b) == id);
        void Press(string id) { Click(Find(id)); quick.UpdateLayout(); }
        Slider Slider() => Descendants<Slider>(quick).Single(s => AutomationProperties.GetAutomationId(s) == "quick-multi-brightness");
        string Amount() => Descendants<TextBlock>(quick).Single(t => AutomationProperties.GetAutomationId(t) == "quick-multi-amount").Text;
        try
        {
            window.Hide(); quick.Render(); quick.Show(); quick.UpdateLayout();
            Press("quick-multi-toggle");
            Check(quick.IsBrightnessMultiSelect && quick.SelectedBrightnessIds.SequenceEqual(new[] { second.Id }) && writes == 0,
                "Entering multi-select starts with the single-screen target and sends no brightness command");
            Check(Descendants<Slider>(quick).Count() == 1 && !Descendants<Button>(quick).Any(b => AutomationProperties.GetAutomationId(b).StartsWith("quick-more-toggle-")) && !Descendants<ComboBox>(quick).Any(),
                "Multi-select exposes only brightness, with scaling and advanced controls absent");
            SelectQuick(quick, 1);
            Check(quick.SelectedBrightnessIds.Count == 2 && Amount() == L.T("quick.multiMixed") && first.Brightness == 50 && second.Brightness == 80 && writes == 0,
                "Selecting different levels shows a mixed value without changing either display");
            Snapshot(quick, "quick-multi-mixed.png");
            Press("quick-multi-all"); Check(quick.SelectedBrightnessIds.Count == 3 && writes == 0, "Select all changes membership only");
            Press("quick-multi-all"); Check(quick.SelectedBrightnessIds.Count == 0 && !Slider().IsEnabled, "Deselect all disables brightness instead of targeting an implicit screen");
            SelectQuick(quick, 1); SelectQuick(quick, 2);
            var slider = Slider(); hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            slider.Value = 61; await Task.Delay(150);
            Check(writes == 1 && lastIds.ToHashSet().SetEquals(new[] { first.Id, second.Id }) && lastValue == 61, "Brightness sends the exact checked identity set");
            SelectQuick(quick, 3); Press("quick-multi-toggle");
            Check(quick.IsBrightnessMultiSelect && quick.SelectedBrightnessIds.Count == 2, "Pending batch blocks membership changes and mode exit");
            slider.Value = 65; slider.Value = 67;
            hold.SetResult(true); hold = null; await Task.Delay(180);
            Check(writes == 2 && lastValue == 67 && third.Brightness == 20, "Queued drags coalesce and never touch an unchecked sync-group member");
            Press("quick-multi-toggle"); Check(quick.IsBrightnessMultiSelect, "Completing a batch mid-drag retains the gesture guard");
            slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent }); await Task.Delay(70); quick.UpdateLayout();
            Check(Amount() == "67%", "Readback shows the common confirmed level after drag completion");
            SelectQuick(quick, 3); fail = true; Slider().Value = 78; await Task.Delay(160); quick.UpdateLayout();
            Check(Amount() == L.T("quick.multiMixed") && Descendants<TextBlock>(quick).Any(t => t.Text.Contains("Fixture: one selected")),
                "Partial batch failure reports the error and retains actual per-screen levels");
            fail = false; Slider().Value = 69; await Task.Delay(160); quick.UpdateLayout();
            Check(Amount() == "69%" && !Descendants<TextBlock>(quick).Any(t => t.Text.Contains("Fixture: one selected")), "A successful retry clears the error and reads back actual levels");
            Snapshot(quick, "quick-multi-common.png");
            ThemeService.Apply("dark"); Snapshot(quick, "quick-multi-dark.png"); ThemeService.Apply("light");
            Press("quick-multi-toggle");
            Check(!quick.IsBrightnessMultiSelect && quick.SelectedDisplayId == second.Id && Descendants<FrameworkElement>(quick).Any(e => AutomationProperties.GetAutomationId(e) == "quick-more-content-2"),
                "Done restores the original single screen and its expanded advanced settings");
            Press("quick-multi-toggle"); Check(quick.SelectedBrightnessIds.SequenceEqual(new[] { second.Id }), "Re-entering starts with the current single screen rather than a stale multi-selection");
            window.Displays.Remove(second); quick.Render(); quick.UpdateLayout();
            Check(quick.SelectedBrightnessIds.Count == 0 && !Slider().IsEnabled, "Unplugging the only checked screen does not substitute another display");
            window.Displays.Add(second); quick.Render(); quick.UpdateLayout();
            Check(!quick.SelectedBrightnessIds.Contains(second.Id), "Reconnected displays are not silently added to the batch");
            quick.Close();
        }
        finally
        {
            hold?.TrySetResult(true); await Task.Delay(80); quick.Close();
            window.Displays = real; window.Preferences.SyncBrightness = oldSync;
            window.Preferences.QuickSelectedDisplayId = oldSelected; window.Preferences.QuickMoreDisplayId = oldMore;
            foreach (var id in new[] { first.Id, second.Id, third.Id }) window.Preferences.DisplayOptions.Remove(id);
            window.SavePreferences(); ThemeService.Apply("light"); window.Show();
        }
    }
}
