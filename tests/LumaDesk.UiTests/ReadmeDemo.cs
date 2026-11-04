// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal static partial class Program
{
    // Documentation capture: real controls, isolated settings, no hardware writes.
    private static async Task CaptureReadmeDemoAsync(MainWindow window)
    {
        Snapshot(window, "settings.png");
        var real = window.Displays;
        window.Displays = new()
        {
            new() { Id = "demo-left", Number = 1, Name = "Studio 27", X = -2560, Current = new(2560, 1440, 144), Brightness = 60, DpiScale = DpiRules.FromRelative(0, 0, 2) },
            new() { Id = "demo-main", Number = 2, Name = "Studio 32", Primary = true, Current = new(3840, 2160, 60), Brightness = 60, DpiScale = DpiRules.FromRelative(0, 1, 2) },
            new() { Id = "demo-portrait", Number = 3, Name = "Portrait", X = 3840, Current = new(1440, 2560, 60), Brightness = 60, DpiScale = DpiRules.FromRelative(0, 0, 2) }
        };
        window.Preferences.QuickSelectedDisplayId = "demo-main";
        window.Preferences.QuickMoreDisplayId = null;
        window.Preferences.SyncBrightness = false;
        Task SetBrightness(DisplayInfo display, int value)
        {
            var split = BrightnessRules.Split(value, true);
            display.Brightness = split.Hardware; display.Dimming = split.Dimming;
            return Task.CompletedTask;
        }
        var quick = new QuickWindow(window, (_, _, _) => Task.CompletedTask, dismissOnDeactivate: false,
            applyBrightness: SetBrightness,
            applySelectedBrightness: async (ids, value) =>
            {
                foreach (var display in window.Displays.Where(d => ids.Contains(d.Id))) await SetBrightness(display, value);
            });
        int frame = 0;
        using var timeline = new StreamWriter(Path.Combine(output, "frames.tsv"));
        async Task Frame(int duration, string stage)
        {
            await Task.Delay(100); quick.UpdateLayout();
            string name = $"frame-{frame++:D3}.png";
            Snapshot(quick, name); timeline.WriteLine($"{name}\t{duration}\t{stage}");
        }
        void Press(string id) => Click(Descendants<Button>(quick).Single(b => AutomationProperties.GetAutomationId(b) == id));
        try
        {
            window.Hide(); quick.Render(); quick.Show();
            await Frame(1600, "select"); Snapshot(quick, "quick-panel.png");
            SelectQuick(quick, 1); await Frame(1000, "select");
            SelectQuick(quick, 3); await Frame(1000, "select");
            SelectQuick(quick, 2); Press("quick-more-toggle-2"); await Frame(2000, "more");
            Press("quick-more-toggle-2"); Press("quick-multi-toggle"); await Frame(1000, "link");
            SelectQuick(quick, 1); await Frame(800, "link");
            SelectQuick(quick, 3); await Frame(800, "link");
            var slider = Descendants<Slider>(quick).Single(s => AutomationProperties.GetAutomationId(s) == "quick-multi-brightness");
            slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            foreach (int value in Enumerable.Range(0, 25).Select(i => 68 - i * 2))
            {
                slider.Value = value; await Frame(70, "adjust");
            }
            foreach (int value in Enumerable.Range(0, 21).Select(i => 20 + i * 3))
            {
                slider.Value = value; await Frame(70, "adjust");
            }
            slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Frame(1600, "adjust");
            Press("quick-multi-toggle"); await Frame(1000, "done");
            ThemeService.Apply("dark"); await Frame(1800, "theme"); Snapshot(quick, "quick-panel-dark.png");
            ThemeService.Apply("light"); await Frame(800, "done");
        }
        finally { quick.Close(); window.Displays = real; }
    }
}
