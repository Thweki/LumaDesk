// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal UIElement? CreateScanModeControls(DisplayInfo display, Func<DisplayInfo, Task<MonitorScanModes>>? read = null,
        Func<DisplayInfo, uint, Task<MonitorScanModes>>? write = null)
    {
        if (!ScanModeRules.Enabled) return null;
        read ??= Service.ReadScanModesAsync; write ??= Service.SetScanModeAsync;
        var content = new StackPanel();
        var expander = new Expander { Header = L.T("scan.title"), Content = content, Margin = new Thickness(0, 5, 0, 5) };
        var choices = new ComboBox { Margin = new Thickness(0, 6, 0, 6) };
        var status = Ui.Text("", 11); status.TextWrapping = TextWrapping.Wrap;
        var help = Ui.Text(L.T("scan.help"), 11, "#7A8699"); help.TextWrapping = TextWrapping.Wrap;
        var apply = Ui.Button(L.T("scan.apply"), () => { });
        var refresh = Ui.Button(L.T("input.refresh"), () => { });
        AutomationProperties.SetAutomationId(expander, "scan-section-" + display.Number);
        AutomationProperties.SetAutomationId(choices, "scan-choice-" + display.Number);
        AutomationProperties.SetName(choices, L.T("scan.title"));
        AutomationProperties.SetAutomationId(apply, "scan-apply-" + display.Number);
        AutomationProperties.SetAutomationId(refresh, "scan-refresh-" + display.Number);
        AutomationProperties.SetAutomationId(status, "scan-status-" + display.Number);
        content.Children.Add(help); content.Children.Add(choices); content.Children.Add(Ui.Row(apply, refresh)); content.Children.Add(status);
        MonitorScanModes? state = null; bool busy = false;
        void Update()
        {
            choices.IsEnabled = !busy && state?.Current != null && state.Choices.Count > 1;
            apply.IsEnabled = choices.IsEnabled && choices.SelectedItem is MonitorScanMode selected && selected.Code != state?.Current;
            refresh.IsEnabled = !busy;
        }
        void SetState(MonitorScanModes? value)
        {
            state = value; choices.ItemsSource = state?.Choices;
            choices.SelectedItem = state?.Choices.FirstOrDefault(c => c.Code == state.Current);
        }
        async Task Load()
        {
            if (busy) return;
            busy = true; status.Text = L.T("sd69db33602"); Update();
            try
            {
                SetState(await read(display));
                status.Text = state?.Choices.Count > 1 ? "" : L.T("scan.unavailable");
            }
            catch (Exception ex) { SetState(null); status.Text = ex.Message; }
            finally { busy = false; Update(); }
        }
        apply.Click += async (_, _) =>
        {
            if (!apply.IsEnabled || busy || choices.SelectedItem is not MonitorScanMode selected) return;
            busy = true; status.Text = L.T("sd69db33602"); Update();
            try { SetState(await write(display, selected.Code)); status.Text = L.T("scan.confirmed"); }
            catch (Exception ex)
            {
                // Failed/unreadable writes must never leave an assumed current value.
                SetState(null); status.Text = ex.Message;
            }
            finally { busy = false; Update(); }
        };
        choices.SelectionChanged += (_, _) => Update();
        refresh.Click += async (_, _) => await Load();
        expander.Expanded += async (_, _) => await Load();
        Update(); return expander;
    }
}
