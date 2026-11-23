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
    private static async Task TestVolumeStepsFixtureAsync(MainWindow window)
    {
        var display = new DisplayInfo { Id = "volume-steps", Volume = 50, Brightness = 30, Dimming = 12 };
        var writes = new List<int>();
        TaskCompletionSource<bool>? hold = null;
        bool fail = false;
        int active = 0, maxActive = 0;
        var control = (FrameworkElement)window.CreateQuickVolumeSlider(display, async value =>
        {
            writes.Add(value); maxActive = Math.Max(maxActive, ++active);
            try
            {
                if (hold != null) await hold.Task;
                if (fail) throw new InvalidOperationException("Fixture volume step failed");
                display.Volume = value;
            }
            finally { active--; }
        });
        var slider = Descendants<Slider>(control).Single();
        Button Find(string id) => Descendants<Button>(control).Single(b => AutomationProperties.GetAutomationId(b) == id);
        var down = Find("quick-volume-down"); var up = Find("quick-volume-up");
        async Task SetLevel(int value)
        {
            slider.Value = value; await Task.Delay(100); writes.Clear();
        }
        try
        {
            control.Measure(new Size(310, 100)); control.Arrange(new Rect(0, 0, 310, 100)); control.UpdateLayout();
            Check(slider.Value == 50 && writes.Count == 0 && up.IsEnabled && down.IsEnabled,
                "Volume steps start at monitor volume without writing on construction");
            Check(AutomationProperties.GetName(up) == L.T("quick.volumeUp") &&
                AutomationProperties.GetName(down) == L.T("quick.volumeDown") &&
                (string)up.ToolTip == L.T("quick.volumeUp") && (string)down.ToolTip == L.T("quick.volumeDown"),
                "Volume step buttons expose localized names and tooltips");
            Check(slider.ActualWidth > 150 && down.ActualWidth >= 36 && up.ActualWidth >= 36,
                "Volume step buttons leave a usable compact slider");

            hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(up); await Task.Delay(100);
            Check(writes.SequenceEqual(new[] { 55 }) && display.Volume == 50,
                "Volume up requests five percentage points and waits for hardware success");
            Click(up); Click(up); Click(down);
            Check(slider.Value == 60 && writes.Count == 1,
                "Rapid volume steps accumulate and reverse from the pending thumb value");
            hold.SetResult(true); hold = null; await Task.Delay(150);
            Check(writes.SequenceEqual(new[] { 55, 60 }) && maxActive == 1 && display.Volume == 60,
                "Slow volume writes serialize and coalesce to the latest target");

            hold = new(TaskCreationOptions.RunContinuationsAsynchronously); writes.Clear();
            Click(down); await Task.Delay(100);
            slider.Value = 41; Click(up);
            hold.SetResult(true); hold = null; await Task.Delay(150);
            Check(writes.SequenceEqual(new[] { 55, 46 }) && slider.Value == 46 && display.Volume == 46 && maxActive == 1,
                "Volume dragging and stepping share the same pending value and write queue");

            var options = window.Options(display); options.Minimum = 20; options.Maximum = 80;
            await SetLevel(98); Click(up); await Task.Delay(100);
            Check(slider.Value == 100 && display.Volume == 100 && !up.IsEnabled && down.IsEnabled && writes.SequenceEqual(new[] { 100 }),
                "Volume up clamps at 100 independently of brightness limits");
            Click(up); await Task.Delay(60);
            Check(writes.Count == 1, "Volume up at the upper boundary sends no redundant write");
            await SetLevel(2); Click(down); await Task.Delay(100);
            Check(slider.Value == 0 && display.Volume == 0 && !down.IsEnabled && up.IsEnabled && writes.SequenceEqual(new[] { 0 }),
                "Volume down clamps at zero independently of brightness limits");
            Click(down); await Task.Delay(60);
            Check(writes.Count == 1 && display.Brightness == 30 && display.Dimming == 12,
                "Volume boundaries do not send duplicate writes or change brightness and dimming");

            await SetLevel(50); fail = true; Click(up); await Task.Delay(100);
            Check(slider.Value == 50 && display.Volume == 50 &&
                Descendants<TextBlock>(control).Any(t => t.ToolTip as string == "Fixture volume step failed"),
                "Failed volume step restores confirmed volume and exposes the error");
            fail = false; Click(down); await Task.Delay(100);
            Check(slider.Value == 45 && display.Volume == 45 && writes.SequenceEqual(new[] { 55, 45 }) &&
                !Descendants<TextBlock>(control).Any(t => t.ToolTip as string == "Fixture volume step failed"),
                "Volume retry starts from the confirmed level and clears the failure");

            var unsupported = window.CreateQuickVolumeSlider(new DisplayInfo(), _ => Task.CompletedTask);
            var settings = window.CreateSlider(L.T("s716ddda53d"), 50, 100, _ => Task.CompletedTask);
            var contrast = window.CreateSlider(L.T("s4cbc753240"), 50, 100, _ => Task.CompletedTask, compact: true);
            Check(!Descendants<Button>(unsupported).Any() && !Descendants<Slider>(unsupported).Single().IsEnabled &&
                !Descendants<Button>(settings).Any() && !Descendants<Button>(contrast).Any(),
                "Volume steps require supported compact volume and leave other sliders unchanged");

            writes.Clear(); Click(up);
            slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); await Task.Delay(100);
            Check(writes.Count == 0 && display.Volume == 45, "Unloading volume controls cancels an unsent step");
        }
        finally
        {
            hold?.TrySetResult(true); await Task.Delay(100);
            slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            window.Preferences.DisplayOptions.Remove(display.Id);
        }
    }
}
