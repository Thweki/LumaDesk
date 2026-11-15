// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal UIElement? CreateRefreshTimingControls(DisplayInfo display, Func<DisplayInfo, Task<RefreshTiming>>? read = null)
    {
        if (!RefreshTimingService.IsEnabled) return null;
        read ??= d => Task.Run(() => RefreshTimingService.Read(d));
        var content = new StackPanel();
        var expander = new Expander { Header = L.T("timing.title"), Content = content, Margin = new Thickness(0, 7, 0, 7) };
        AutomationProperties.SetAutomationId(expander, "timing-section-" + display.Number);
        var status = Ui.Text("", 12); status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(status, "timing-status-" + display.Number);
        var help = Ui.Text(L.T("timing.help"), 11, "#7A8699"); help.TextWrapping = TextWrapping.Wrap;
        var refresh = Ui.Button(L.T("sa13550662f"), () => { });
        AutomationProperties.SetAutomationId(refresh, "timing-refresh-" + display.Number);
        content.Children.Add(status); content.Children.Add(help); content.Children.Add(refresh);
        bool loading = false;
        string Rate(double? hz) => hz.HasValue ? L.F("timing.hz", hz.Value) : L.T("s4d8c1c5b42");
        async Task Load()
        {
            if (loading) return;
            loading = true; refresh.IsEnabled = false; status.Text = L.T("sd69db33602");
            try
            {
                var timing = await read(display);
                status.Text = timing.Error.Length > 0 ? timing.Error :
                    L.F("timing.rates", Rate(timing.DesktopHz), Rate(timing.SignalHz)) + "\n" +
                    L.T(timing.DynamicRefreshEnabled switch { true => "timing.on", false => "timing.off", _ => "timing.unknown" });
            }
            catch (Exception ex) { status.Text = L.F("timing.failed", ex.Message); }
            finally { loading = false; refresh.IsEnabled = true; }
        }
        expander.Expanded += async (_, _) => await Load();
        refresh.Click += async (_, _) => await Load();
        return expander;
    }
}
