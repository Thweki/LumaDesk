// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    private readonly NightDimming nightDimming = new();
    private bool nightDimmingRetry;

    internal void UpdateNightDimming(DateTime now, bool force = false, Action<DisplayInfo, double>? applyOverlay = null)
    {
        bool changed = nightDimming.Update(Preferences.ExperimentalNightDimming,
            Preferences.NightDimmingStart, Preferences.NightDimmingEnd, now);
        if (changed || (force && nightDimming.IsActive) || nightDimmingRetry)
            RefreshNightDimmingOverlays(applyOverlay);
    }

    private void RefreshNightDimmingOverlays(Action<DisplayInfo, double>? applyOverlay = null)
    {
        nightDimmingRetry = false;
        foreach (var display in Displays)
        {
            try { (applyOverlay ?? ApplyDimmingOverlay)(display, nightDimming.EffectiveDimming(display.Dimming)); }
            catch (Exception ex) { nightDimmingRetry = true; Status(ex.Message, true); }
        }
    }

    internal void SuspendNightDimming(DateTime now, Action<DisplayInfo, double>? applyOverlay = null)
    {
        bool wasActive = nightDimming.IsActive;
        nightDimming.Update(Preferences.ExperimentalNightDimming,
            Preferences.NightDimmingStart, Preferences.NightDimmingEnd, now);
        bool active = nightDimming.IsActive;
        nightDimming.Suspend();
        if (wasActive || active || nightDimmingRetry) RefreshNightDimmingOverlays(applyOverlay);
    }

    internal UIElement CreateNightDimmingOptions()
    {
        var content = new StackPanel();
        var enabled = Toggle(Preferences.ExperimentalNightDimming, value =>
        {
            Preferences.ExperimentalNightDimming = value; Save(); UpdateNightDimming(DateTime.Now);
        });
        AutomationProperties.SetAutomationId(enabled, "night-dimming-enabled");
        content.Children.Add(Ui.Row(Ui.Labels(L.T("night.title"), L.T("night.help")), enabled));
        var start = new TextBox { Text = Preferences.NightDimmingStart, Width = 65, MaxLength = 5 };
        var end = new TextBox { Text = Preferences.NightDimmingEnd, Width = 65, MaxLength = 5 };
        AutomationProperties.SetAutomationId(start, "night-dimming-start");
        AutomationProperties.SetName(start, L.T("night.start"));
        AutomationProperties.SetAutomationId(end, "night-dimming-end");
        AutomationProperties.SetName(end, L.T("night.end"));
        var save = Ui.Button(L.T("night.apply"), () =>
        {
            if (!NightDimming.ValidWindow(start.Text, end.Text)) { Status(L.T("night.invalid"), true); return; }
            Preferences.NightDimmingStart = start.Text; Preferences.NightDimmingEnd = end.Text;
            Save(); UpdateNightDimming(DateTime.Now);
        });
        AutomationProperties.SetAutomationId(save, "night-dimming-apply");
        content.Children.Add(Ui.Line());
        content.Children.Add(Ui.Row(Ui.Text(L.T("night.start")), start));
        content.Children.Add(Ui.Row(Ui.Text(L.T("night.end")), end));
        content.Children.Add(save);
        return Ui.Card(content);
    }
}
