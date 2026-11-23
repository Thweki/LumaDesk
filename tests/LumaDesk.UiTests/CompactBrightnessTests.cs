// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

internal static partial class Program
{
    private static async Task TestCompactBrightnessFixtureAsync(MainWindow window)
    {
        var display = new DisplayInfo { Id = "compact-slider", Number = 99, Brightness = 50 };
        var writes = new List<int>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control = (FrameworkElement)window.CreateBrightnessSlider(display, async (target, value) =>
        {
            Check(ReferenceEquals(target, display), "Compact brightness retains its display identity");
            writes.Add(value);
            await release.Task;
            var level = BrightnessRules.Adjust(value, target.Brightness, target.Dimming);
            target.Brightness = level.Hardware; target.Dimming = level.Dimming;
        }, compact: true);
        var slider = Descendants<Slider>(control).Single();
        try
        {
            control.Measure(new Size(310, 100)); control.Arrange(new Rect(0, 0, 310, 100)); control.UpdateLayout();
            Check(slider.Value == 60 && writes.Count == 0, "Compact slider reads the combined level without writing");
            Check(!Descendants<Button>(control).Any() && slider.ActualWidth >= 308,
                "Brightness uses the full row without flanking step buttons");
            slider.Value = 65; await Task.Delay(100);
            slider.Value = 71; slider.Value = 73;
            Check(writes.SequenceEqual(new[] { 65 }), "Further movement waits for a pending brightness write");
            release.SetResult(); await Task.Delay(150);
            Check(writes.SequenceEqual(new[] { 65, 73 }) && slider.Value == 73, "Compact slider commits the latest pending level");
            slider.Value = 42;
            slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); await Task.Delay(100);
            Check(writes.Count == 2, "Unloading the slider cancels an unsent write");
        }
        finally { release.TrySetResult(); }
    }
}
