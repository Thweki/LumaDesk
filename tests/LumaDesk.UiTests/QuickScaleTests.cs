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
    private static async Task TestQuickScaleFixtureAsync(MainWindow owner)
    {
        var display = new DisplayInfo { Id = "scale-fixture", Number = 91, Name = "Scale fixture",
            Current = new(3840, 2160, 144), Orientation = 1, DpiScale = DpiRules.FromRelative(0, 6, 7) };
        var other = new DisplayInfo { Id = "scale-other", Number = 92, DpiScale = DpiRules.FromRelative(0, 1, 2) };
        var originalMode = display.Current;
        var writes = new List<int>();
        int active = 0, peak = 0;
        bool fail = false;
        TaskCompletionSource<bool>? hold = null;
        QuickWindow? requestedOwner = null;
        DisplayInfo? requestedDisplay = null;
        var quick = new QuickWindow(owner, async (target, percent, parent) =>
        {
            writes.Add(percent); active++; peak = Math.Max(peak, active);
            requestedDisplay = target; requestedOwner = parent as QuickWindow;
            try
            {
                if (hold != null) await hold.Task;
                if (fail) throw new InvalidOperationException("Scale fixture rejected");
                target.DpiScale.CurrentPercent = percent;
            }
            finally { active--; }
        }, dismissOnDeactivate: false);
        // Detached controls exercise the real handlers without discovery or a HWND.
        // Suppress the idle panel rebuild; the fixture supplies its own display.
        var lease = quick.KeepOpen();
        Button Button(UIElement host, string action, int number = 91) => Descendants<Button>(host)
            .Single(b => AutomationProperties.GetAutomationId(b) == $"quick-dpi-{action}-{number}");
        try
        {
            var host = (FrameworkElement)quick.CreateDpiControls(display);
            host.Measure(new Size(320, 100)); host.Arrange(new Rect(0, 0, 320, 100));
            var slider = Descendants<Slider>(host).Single();
            var plus = Button(host, "increase"); var minus = Button(host, "decrease");
            Check(writes.Count == 0 && plus.IsEnabled && minus.IsEnabled,
                "Scale nudges start from the current scale without writes");
            Check(AutomationProperties.GetName(plus).Contains(L.T("quick.scaleIncrease")) &&
                AutomationProperties.GetName(minus).Contains(L.T("quick.scaleDecrease")) && plus.Focusable && minus.Focusable,
                "Scale nudges are keyboard accessible with localized action names");
            Click(plus); await Task.Delay(150);
            Check(writes.SequenceEqual(new[] { 300 }) && display.DpiScale.CurrentPercent == 300 && !plus.IsEnabled && minus.IsEnabled,
                "Plus follows the next supported scale from 250 to 300 and disables at the upper limit");
            Click(plus); await Task.Delay(100);
            Check(writes.Count == 1, "Upper boundary cannot generate another scale write");
            Click(minus); await Task.Delay(150);
            Check(writes.SequenceEqual(new[] { 300, 250 }) && ReferenceEquals(requestedDisplay, display) && ReferenceEquals(requestedOwner, quick),
                "Minus targets only its display through the quick panel DPI callback");

            hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(minus); await Task.Delay(150); // In flight: 225%.
            Click(plus); Click(plus); Click(minus); // Latest pending: 250%.
            var otherHost = quick.CreateDpiControls(other);
            Click(Button(otherHost, "increase", 92));
            await Task.Delay(100);
            Check(writes.Count == 3 && writes.Last() == 225 && other.DpiScale.CurrentPercent == 125,
                "Slow writes queue nudges from pending state and block a competing display");
            hold.SetResult(true); hold = null; await Task.Delay(200);
            Check(writes.SequenceEqual(new[] { 300, 250, 225, 250 }) && peak == 1 && display.DpiScale.CurrentPercent == 250,
                "Rapid nudges coalesce to the last target with no overlapping writes");

            fail = true; Click(plus); await Task.Delay(150);
            Check(display.DpiScale.CurrentPercent == 250 && slider.Value == 1 && plus.IsEnabled && minus.IsEnabled &&
                Descendants<TextBlock>(host).Any(t => t.Text == L.F("sdaee699fa0", 250)),
                "Rejected nudge restores the confirmed percentage, slider and button limits");
            fail = false;
            for (int i = 0; i < 12; i++) Click(minus);
            await Task.Delay(150);
            Check(writes.Last() == 100 && display.DpiScale.CurrentPercent == 100 && !minus.IsEnabled && plus.IsEnabled,
                "Repeated minus clicks stop at the lowest supported scale");
            int count = writes.Count; Click(minus); await Task.Delay(100);
            Check(writes.Count == count, "Lower boundary cannot generate another scale write");

            var single = quick.CreateDpiControls(new DisplayInfo { DpiScale = DpiRules.FromRelative(0, 0, 0) });
            Check(Descendants<Button>(single).Count() == 2 && Descendants<Button>(single).All(b => !b.IsEnabled),
                "A single supported scale disables both nudges");
            var unsupported = quick.CreateDpiControls(new DisplayInfo { DpiScale = new() { UnavailableReason = "Custom scaling" } });
            Check(!Descendants<Button>(unsupported).Any() && !Descendants<Slider>(unsupported).Any() &&
                Descendants<TextBlock>(unsupported).Any(t => t.Text == "Custom scaling"),
                "Unsupported scaling explains availability without actionable controls");
            Check(display.Current == originalMode && display.Orientation == 1 && other.DpiScale.CurrentPercent == 125 && !owner.IsVisible,
                "Nudges leave output mode, rotation, other displays and full settings alone");
        }
        finally
        {
            hold?.TrySetResult(true); await Task.Delay(150); lease.Dispose(); quick.Close();
        }
    }
}
