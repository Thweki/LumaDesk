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

internal static partial class Program
{
    private static async Task TestBrightnessStepsFixtureAsync(MainWindow window)
    {
        var real = window.Displays;
        var display = new DisplayInfo { Id = "brightness-steps", Number = 99, Brightness = 50 };
        window.Displays = new() { display };
        var writes = new List<int>();
        TaskCompletionSource<bool>? hold = null;
        bool fail = false;
        int active = 0, maxActive = 0;
        var control = (FrameworkElement)window.CreateBrightnessSlider(display, async (target, value) =>
        {
            Check(ReferenceEquals(target, display), "Step callback retains the selected display identity");
            writes.Add(value); maxActive = Math.Max(maxActive, ++active);
            try
            {
                if (hold != null) await hold.Task;
                if (fail) throw new InvalidOperationException("Fixture brightness step failed");
                var level = BrightnessRules.Adjust(value, target.Brightness, target.Dimming);
                target.Brightness = level.Hardware; target.Dimming = level.Dimming;
            }
            finally { active--; }
        }, compact: true);
        var slider = Descendants<Slider>(control).Single();
        Button Find(string id) => Descendants<Button>(control).Single(b => AutomationProperties.GetAutomationId(b) == id);
        var down = Find("quick-brightness-down"); var up = Find("quick-brightness-up");
        async Task SetLevel(int value)
        {
            slider.Value = value; await Task.Delay(100); writes.Clear();
        }
        try
        {
            control.Measure(new Size(310, 100)); control.Arrange(new Rect(0, 0, 310, 100)); control.UpdateLayout();
            Check(slider.Value == 60 && writes.Count == 0 && up.IsEnabled && down.IsEnabled,
                "Step controls start at combined brightness without writing on construction");
            Check(AutomationProperties.GetName(up) == L.T("s768e8a9c2a") && AutomationProperties.GetName(down) == L.T("s970eb36fb3") &&
                (string)up.ToolTip == L.T("s768e8a9c2a"), "Step buttons expose localized names and tooltips");
            Check(slider.ActualWidth > 150 && down.ActualWidth >= 36 && up.ActualWidth >= 36,
                "Step buttons leave a usable slider in the compact panel");

            hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(up); await Task.Delay(100);
            Check(writes.SequenceEqual(new[] { 65 }), "A step adds five combined points, not five backlight points");
            Click(up); Click(up); Click(down);
            Check(slider.Value == 70 && writes.Count == 1, "Rapid steps accumulate and reverse from the pending thumb value");
            hold.SetResult(true); hold = null; await Task.Delay(150);
            Check(writes.SequenceEqual(new[] { 65, 70 }) && maxActive == 1 && slider.Value == 70,
                "Slow step writes serialize and coalesce without losing the final target");

            hold = new(TaskCreationOptions.RunContinuationsAsynchronously); writes.Clear();
            Click(down); await Task.Delay(100);
            slider.Value = 41; Click(up);
            hold.SetResult(true); hold = null; await Task.Delay(150);
            Check(writes.SequenceEqual(new[] { 65, 46 }) && slider.Value == 46 && maxActive == 1,
                "Dragging and stepping share one pending value and write queue");

            var options = window.Options(display); options.Minimum = 22; options.Maximum = 73;
            await SetLevel(71); Click(up); await Task.Delay(100);
            Check(slider.Value == 73 && !up.IsEnabled && down.IsEnabled && writes.SequenceEqual(new[] { 73 }),
                "Up clamps to the display maximum and disables further steps");
            Click(up); await Task.Delay(60);
            Check(writes.Count == 1, "A step at the upper boundary sends no redundant write");
            await SetLevel(24); Click(down); await Task.Delay(100);
            Check(slider.Value == 22 && !down.IsEnabled && up.IsEnabled && writes.SequenceEqual(new[] { 22 }),
                "Down clamps to the display minimum and disables further steps");

            options.Minimum = 0; options.Maximum = 100;
            foreach (bool hardware in new[] { true, false })
            {
                display.Brightness = hardware ? 0 : null;
                await SetLevel(22); Click(down); await Task.Delay(100);
                Check(slider.Value == 17 && display.Dimming > 0 && display.Brightness == (hardware ? 0 : (int?)null),
                    "Steps cross into software dimming, hardware=" + hardware);
                Click(up); await Task.Delay(100);
                Check(slider.Value == 22 && window.GetCombinedBrightness(display) == 22,
                    "A reverse step restores the combined level, hardware=" + hardware);
                await SetLevel(2); Click(down); await Task.Delay(100);
                Check(slider.Value == 0 && display.Dimming == 85 && !down.IsEnabled,
                    "Zero stops at maximum supported dimming, hardware=" + hardware);
                await SetLevel(98); Click(up); await Task.Delay(100);
                Check(slider.Value == 100 && display.Dimming == 0 && !up.IsEnabled,
                    "Full brightness clears dimming and disables up, hardware=" + hardware);
            }

            await SetLevel(50); fail = true; Click(up); await Task.Delay(100);
            Check(slider.Value == 50 && window.GetCombinedBrightness(display) == 50 &&
                Descendants<TextBlock>(control).Any(t => t.ToolTip as string == "Fixture brightness step failed"),
                "Failed step restores confirmed brightness and exposes the error");
            fail = false; Click(down); await Task.Delay(100);
            Check(slider.Value == 45 && writes.SequenceEqual(new[] { 55, 45 }) &&
                !Descendants<TextBlock>(control).Any(t => t.ToolTip as string == "Fixture brightness step failed"),
                "Retry starts from confirmed brightness and clears the step failure");

            var settings = window.CreateBrightnessSlider(display, (_, _) => Task.CompletedTask);
            var volume = window.CreateSlider("volume", 50, 100, _ => Task.CompletedTask, compact: true);
            Check(!Descendants<Button>(settings).Any() && !Descendants<Button>(volume).Any(),
                "Brightness steps leave full settings and generic compact sliders unchanged");

            writes.Clear(); Click(up);
            slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); await Task.Delay(100);
            Check(writes.Count == 0, "Unloading step controls cancels an unsent request");
        }
        finally
        {
            hold?.TrySetResult(true); await Task.Delay(100);
            slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            window.Displays = real; window.Preferences.DisplayOptions.Remove(display.Id);
        }
    }
}
