// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LumaDesk;

internal sealed partial class QuickWindow
{
    private readonly TextBlock scheduleStatus = Ui.Text("", 11, "#748095");
    private readonly DispatcherTimer scheduleStatusTimer = new() { Interval = TimeSpan.FromSeconds(20) };

    private void InitializeScheduleStatus()
    {
        scheduleStatus.Margin = new Thickness(12, 6, 12, 0);
        scheduleStatus.TextWrapping = TextWrapping.Wrap;
        scheduleStatus.ToolTip = L.T("automation.nextHint");
        AutomationProperties.SetAutomationId(scheduleStatus, "quick-next-schedule");
        scheduleStatusTimer.Tick += (_, _) => RefreshScheduleStatus(DateTime.Now);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { RefreshScheduleStatus(DateTime.Now); scheduleStatusTimer.Start(); }
            else scheduleStatusTimer.Stop();
        };
        Closed += (_, _) => scheduleStatusTimer.Stop();
    }

    internal void RefreshScheduleStatus(DateTime now)
    {
        var next = AutomationRules.NextScheduledPreset(owner.Preferences.Presets, owner.Displays, now);
        // Update only this label: a clock tick must not rebuild an active slider or menu.
        scheduleStatus.Text = next is { } scheduled
            ? L.F("automation.next", scheduled.Preset.Name, scheduled.At.ToString("g")) : "";
        scheduleStatus.Visibility = next.HasValue ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(scheduleStatus, scheduleStatus.Text);
    }
}
