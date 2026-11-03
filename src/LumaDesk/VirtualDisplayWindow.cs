// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

internal sealed class VirtualDisplayWindow : Window
{
    private readonly MainWindow owner;
    private readonly VirtualDisplayService service;
    private readonly StackPanel body = new() { Margin = new Thickness(20) };
    private readonly TextBlock feedback = Ui.Text(L.T("s1bafd2539d"), 12, "#68768A");
    private bool busy;
    internal VirtualDisplayWindow(MainWindow owner, VirtualDisplayService? service = null)
    {
        Ui.ConfigureWindow(this);
        this.owner = owner; this.service = service ?? new VirtualDisplayService();
        Title = L.T("s5cf23a742f"); Width = 540; Height = 530; MinWidth = 440; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer { Content = body };
        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private async Task RefreshAsync(string? message = null)
    {
        body.Children.Clear(); body.Children.Add(Ui.Text(L.T("s1b4f0e57f8"), 21, "#28364A", true));
        var description = Ui.Text(L.T("s88ff52ede0"), 12, "#7A8595"); description.Margin = new Thickness(0, 9, 0, 16); body.Children.Add(description);
        try
        {
            VirtualDisplayState state = await service.ReadAsync();
            var panel = new StackPanel();
            panel.Children.Add(Ui.Row(Ui.Labels("Virtual Display Driver", state.Message), Ui.Badge(state.Connected ? L.T("s5be0323e8a") : state.Installed ? L.T("s4afcbf8b01") : L.T("s156219e305"))));
            panel.Children.Add(Ui.Line());
            panel.Children.Add(Ui.Labels(L.T("s6e13d9980c"), state.ActiveCount + L.T("sdb5ed74bd6") + (state.ConfiguredCount.HasValue ? L.F("s2eb9fe8ee3", state.ConfiguredCount) : "")));
            if (!state.Installed)
            {
                var install = Ui.Button(L.T("s5e743b731e"), () => _ = InstallAsync(), true); install.Margin = new Thickness(0, 15, 0, 0);
                System.Windows.Automation.AutomationProperties.SetAutomationId(install, "virtual-driver-install");
                panel.Children.Add(install);
                var note = Ui.Text(L.F("s79ca4fc030", DriverInstaller.Version), 11, "#7A8595"); note.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(note);
            }
            else
            {
                var count = new ComboBox { ItemsSource = Enumerable.Range(0, VirtualDisplayService.MaximumDisplays + 1), SelectedItem = state.Disabled ? 0 : Math.Clamp(state.ConfiguredCount ?? state.ActiveCount, 0, VirtualDisplayService.MaximumDisplays), MinWidth = 70, Width = 80 };
                System.Windows.Automation.AutomationProperties.SetAutomationId(count, "virtual-display-count");
                var set = Ui.Button(L.T("s1d4b09076c"), () => _ = SetCountAsync((int)count.SelectedItem), true);
                System.Windows.Automation.AutomationProperties.SetAutomationId(set, "virtual-display-apply-count");
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), IsEnabled = state.Connected || state.Disabled };
                row.Children.Add(count); set.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(set); panel.Children.Add(row);
                var note = Ui.Text(L.T("s664e61e05e"), 11, "#7A8595"); note.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(note);
                panel.Children.Add(Ui.Line());
                var actions = Ui.Row(Ui.Button(L.T("sa1babf9ee8"), owner.ShowLayout), Ui.Button(L.T("sa13550662f"), () => _ = RefreshAsync())); panel.Children.Add(actions);
            }
            body.Children.Add(Ui.Card(panel));
            var limit = Ui.Text(L.T("s151dd3bdaf"), 11, "#7A8595"); body.Children.Add(limit);
            feedback.Text = message ?? (state.Installed ? "" : L.T("s96da487660")); feedback.Margin = new Thickness(0, 15, 0, 0); body.Children.Add(feedback);
        }
        catch (Exception ex) { feedback.Text = ex.Message; if (!body.Children.Contains(feedback)) body.Children.Add(feedback); }
    }
    private async Task InstallAsync()
    {
        if (busy) return; busy = true; body.IsEnabled = false; string message;
        try
        {
            feedback.Text = L.T("sb8b437fdaf");
            string package = await DriverInstaller.PreparePackageAsync();
            feedback.Text = L.T("s1165fd2060");
            await DriverInstaller.InstallAsync(package);
            await Task.Delay(1500); await owner.RefreshAsync();
            message = L.T("sf78933bd14");
        }
        catch (Exception ex) { message = ex.Message; Storage.Log("Virtual driver install: " + ex); }
        finally { busy = false; body.IsEnabled = true; }
        await RefreshAsync(message);
    }
    private async Task SetCountAsync(int count)
    {
        if (busy) return; busy = true; body.IsEnabled = false; string message;
        try { feedback.Text = L.T("s608dbb7aa7"); await service.SetCountAsync(count); message = L.T("seb070c1fdd") + count + L.T("sfa28415f47"); }
        catch (Exception ex) { message = ex.Message; Storage.Log("Virtual monitor count: " + ex); }
        finally { busy = false; body.IsEnabled = true; await owner.RefreshAsync(); }
        await RefreshAsync(message);
    }
}
