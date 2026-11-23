// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace LumaDesk;

internal sealed partial class QuickWindow
{
    private readonly PanelKeepAwake keepAwake;
    private string? keepAwakeError;

    private void RenderKeepAwake()
    {
        if (!keepAwake.Available) return;
        var toggle = new CheckBox
        {
            Content = L.T("awake.toggle"), IsChecked = keepAwake.Active,
            Margin = new Thickness(12, 8, 12, 4), FontSize = 12,
            ToolTip = L.T("awake.help")
        };
        AutomationProperties.SetAutomationId(toggle, "quick-keep-awake");
        AutomationProperties.SetName(toggle, L.T("awake.toggle"));
        toggle.Click += (_, _) =>
        {
            keepAwakeError = null;
            try { keepAwake.SetEnabled(toggle.IsChecked == true, IsVisible); }
            catch (Win32Exception ex) { keepAwakeError = L.F("awake.error", ex.Message); }
            toggle.IsChecked = keepAwake.Active;
            Render();
        };
        body.Children.Add(toggle);
        if (keepAwakeError != null)
        {
            var error = Ui.Text(keepAwakeError, 11, "#B45136");
            error.TextWrapping = TextWrapping.Wrap; error.Margin = new Thickness(12, 0, 12, 4);
            AutomationProperties.SetAutomationId(error, "quick-keep-awake-error");
            body.Children.Add(error);
        }
    }
}
