// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

internal static partial class Program
{
    private static Native.MonitorInfo PanelMonitor(Window panel)
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Check(Native.GetMonitorInfo(Native.MonitorFromWindow(new WindowInteropHelper(panel).Handle, 2), ref info),
            "Panel has a connected monitor");
        return info;
    }

    private static void CheckPanelBounds(Window panel, Native.MonitorInfo target, string message)
    {
        Native.GetWindowRect(new WindowInteropHelper(panel).Handle, out var bounds);
        var dpi = VisualTreeHelper.GetDpi(panel);
        Check(bounds.Left >= target.Work.Left && bounds.Top >= target.Work.Top &&
            bounds.Right <= target.Work.Right && bounds.Bottom <= target.Work.Bottom &&
            Math.Abs(target.Work.Right - bounds.Right - Math.Round(12 * dpi.DpiScaleX)) <= 1 &&
            Math.Abs(target.Work.Bottom - bounds.Bottom - Math.Round(12 * dpi.DpiScaleY)) <= 1, message);
    }

    private static async Task TestFocusDisplayAsync(MainWindow window)
    {
        var real = window.Displays;
        string? savedSelected = window.Preferences.QuickSelectedDisplayId, savedMore = window.Preferences.QuickMoreDisplayId;
        var quick = new QuickWindow(window, dismissOnDeactivate: false);
        Button Find(string id) => Descendants<Button>(quick).Single(b => AutomationProperties.GetAutomationId(b) == id);
        window.Hide(); window.Preferences.QuickMoreDisplayId = null;
        try
        {
            quick.Render(); quick.Show(); quick.Activate(); await Task.Delay(100);
            var tray = PanelMonitor(quick);
            var anchor = tray;
            // Exercise actual mixed-DPI/negative-origin screens when the desktop has them.
            foreach (var display in real.OrderBy(d => d.Device != tray.Device))
            {
                SelectQuick(quick, display.Number); await Task.Delay(80);
                Check(PanelMonitor(quick).Device == anchor.Device, "Selecting controls alone does not move the panel");
                var selected = quick.SelectedDisplayId;
                using (quick.KeepOpen())
                {
                    Click(Find("quick-move-panel"));
                    Check(PanelMonitor(quick).Device == anchor.Device, "A busy panel stays on its current monitor");
                }
                Click(Find("quick-move-panel")); await Task.Delay(150);
                anchor = PanelMonitor(quick);
                Check(anchor.Device == display.Device && quick.SelectedDisplayId == selected && quick.IsVisible && !window.IsVisible,
                    "Move panel here targets the selected display without changing selection or opening settings");
                CheckPanelBounds(quick, anchor, "Moved panel fits the selected work area at its current DPI");
                Click(Find("quick-more-toggle-" + display.Number)); await Task.Delay(100);
                CheckPanelBounds(quick, anchor, "Expanding controls keeps the chosen panel anchor");
                Click(Find("quick-more-toggle-" + display.Number)); await Task.Delay(80);
            }

            Click(Find("quick-multi-toggle")); quick.UpdateLayout();
            Check(!Descendants<Button>(quick).Any(b => AutomationProperties.GetAutomationId(b) == "quick-move-panel"),
                "Multi-display brightness mode has no ambiguous move target");
            Click(Find("quick-multi-toggle")); quick.UpdateLayout();
            var staleMove = Find("quick-move-panel");
            window.Displays = new List<DisplayInfo>();
            Click(staleMove);
            Check(PanelMonitor(quick).Device == anchor.Device, "A stale display action leaves the panel in place");
            quick.Render(); quick.UpdateLayout();
            Check(!Descendants<Button>(quick).Any(b => AutomationProperties.GetAutomationId(b) == "quick-move-panel"),
                "An empty display scan removes the move action");

            quick.Close(); window.Displays = real;
            quick = new QuickWindow(window, dismissOnDeactivate: false);
            quick.Render(); quick.Show(); await Task.Delay(100);
            Check(PanelMonitor(quick).Device == tray.Device, "Reopening starts at the tray, independent of the last selected display");
        }
        finally
        {
            quick.Close(); window.Displays = real;
            window.Preferences.QuickSelectedDisplayId = savedSelected; window.Preferences.QuickMoreDisplayId = savedMore;
            window.SavePreferences(); window.Show();
        }

        // Simulate disconnects without changing the desktop or requiring two monitors.
        var host = new Window { Width = 360, Height = 200, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        var fallback = PanelMonitor(window); fallback.Device = "tray";
        var target = fallback; target.Device = "target";
        target.Work.Right -= 48; target.Work.Bottom -= 48;
        bool connected = true, available = true;
        double height = 200;
        var placement = new PopupPlacement(host, () => height, device => !available ? null :
            device == null || device == fallback.Device ? fallback : device == target.Device && connected ? target : null);
        try
        {
            Check(!placement.MoveToMonitor(target.Device), "Moving an unopened panel is rejected");
            host.Show(); await Task.Delay(80);
            Check(placement.MoveToMonitor(target.Device), "A connected explicit target is accepted");
            await Task.Delay(80);
            CheckPanelBounds(host, target, "Explicit target replaces the tray anchor");
            Check(!placement.MoveToMonitor("missing") && !placement.MoveToMonitor(""), "Missing and empty targets are rejected");
            height = 280; placement.Position(); await Task.Delay(80);
            CheckPanelBounds(host, target, "Failed moves and content resizing preserve the selected anchor");
            available = false;
            Check(!placement.MoveToMonitor(target.Device), "Unavailable topology rejects explicit moves");
            placement.Position();
            CheckPanelBounds(host, target, "A transient empty topology preserves the last safe position");
            available = true; placement.Position(); await Task.Delay(80);
            CheckPanelBounds(host, target, "The chosen anchor survives a transient empty topology");
            connected = false; placement.Position(); await Task.Delay(80);
            CheckPanelBounds(host, fallback, "Disconnecting the anchor returns the panel to the tray work area");
            connected = true; placement.Position(); await Task.Delay(80);
            CheckPanelBounds(host, fallback, "Reconnection does not unexpectedly move the panel again");
            host.Close();
            Check(!placement.MoveToMonitor(target.Device), "Moving a closed panel is rejected");
        }
        finally { host.Close(); }
    }
}
