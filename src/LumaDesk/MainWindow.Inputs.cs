// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal UIElement CreateInputControls(DisplayInfo display, Func<DisplayInfo, Task<MonitorInputs>>? read = null,
        Func<DisplayInfo, uint, Task<InputSwitchResult>>? write = null)
    {
        read ??= Service.ReadInputsAsync; write ??= Service.SetInputAsync;
        var content = new StackPanel(); var status = Ui.Text("", 11); status.TextWrapping = TextWrapping.Wrap;
        var expander = new Expander { Header = L.T("input.title"), Content = content, Margin = new Thickness(0, 5, 0, 5) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(expander, "input-section-" + display.Number);
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "input-status-" + display.Number);
        var current = Ui.Text("", 11, "#7A8699");
        var choices = new ComboBox { Margin = new Thickness(0, 6, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(choices, "input-choice-" + display.Number);
        System.Windows.Automation.AutomationProperties.SetName(choices, L.T("input.title"));
        var apply = Ui.Button(L.T("input.switch"), () => { });
        var refresh = Ui.Button(L.T("input.refresh"), () => { });
        var actions = Ui.Row(apply, refresh);
        System.Windows.Automation.AutomationProperties.SetAutomationId(apply, "input-apply-" + display.Number);
        System.Windows.Automation.AutomationProperties.SetAutomationId(refresh, "input-refresh-" + display.Number);
        MonitorInputs? inputs = null; bool loading = false, switching = false;
        void Update()
        {
            var active = inputs?.Choices.FirstOrDefault(i => i.Code == inputs.Current);
            current.Text = active == null ? L.T("input.currentUnknown") : L.F("input.current", active);
            apply.IsEnabled = !loading && !switching && choices.SelectedItem is MonitorInput selected && selected.Code != inputs?.Current;
            choices.IsEnabled = !loading && !switching; refresh.IsEnabled = !loading && !switching;
        }
        async Task Load()
        {
            if (loading || switching) return;
            loading = true; status.Text = L.T("sd69db33602"); Update();
            try
            {
                inputs = await read(display);
                choices.ItemsSource = inputs.Choices;
                choices.SelectedItem = inputs.Choices.FirstOrDefault(i => i.Code == inputs.Current);
                content.Children.Clear();
                content.Children.Add(current); content.Children.Add(choices); content.Children.Add(actions);
                var help = Ui.Text(L.T("input.help"), 11, "#7A8699"); help.TextWrapping = TextWrapping.Wrap; help.Margin = new Thickness(0, 7, 0, 7);
                content.Children.Add(help); content.Children.Add(status);
                status.Text = inputs.Choices.Count == 0 ? L.T("input.unavailable") : inputs.LegacyCodes ? L.T("input.compatibility") : "";
            }
            catch (Exception ex) { inputs = null; choices.ItemsSource = null; status.Text = ex.Message; }
            finally { loading = false; Update(); }
        }
        apply.Click += async (_, _) =>
        {
            if (loading || switching || inputs == null || choices.SelectedItem is not MonitorInput selected || selected.Code == inputs.Current) return;
            using var hold = (Window.GetWindow(expander) as QuickWindow)?.KeepOpen();
            switching = true; status.Text = L.T("input.switching"); Update();
            try
            {
                var result = await write(display, selected.Code);
                inputs = inputs with { Current = result.Confirmed ? result.Current : null };
                status.Text = result.Confirmed ? L.F("input.confirmed", selected) : L.T("input.unverified");
            }
            catch (Exception ex)
            {
                status.Text = ex.Message; Status(ex.Message, true);
                // Invalidate the stale current value, then refresh read-only. In
                // particular, switching back must not be silently skipped.
                inputs = inputs with { Current = null };
                try { inputs = await read(display); choices.ItemsSource = inputs.Choices; choices.SelectedItem = inputs.Choices.FirstOrDefault(i => i.Code == inputs.Current); }
                catch { }
            }
            finally { switching = false; Update(); }
        };
        choices.SelectionChanged += (_, _) => Update();
        refresh.Click += async (_, _) => await Load();
        expander.Expanded += async (_, _) => await Load();
        content.Children.Add(status);
        return expander;
    }
}
