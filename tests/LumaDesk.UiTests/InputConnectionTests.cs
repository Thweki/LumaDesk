// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

internal static partial class Program
{
    private static async Task TestInputConnectionUiAsync(MainWindow window)
    {
        var display = new DisplayInfo { Id = "connection-fixture", Number = 98 };
        var reported = new MonitorInputs(null, Array.Empty<MonitorInput>()) { ConnectionTechnology = 5 };
        int reads = 0, writes = 0; bool fail = false;
        var panel = (Expander)window.CreateInputControls(display,
            _ => { reads++; return fail ? Task.FromException<MonitorInputs>(new InvalidOperationException("Fixture unavailable")) : Task.FromResult(reported); },
            (_, _) => { writes++; return Task.FromResult(new InputSwitchResult(false, null)); });
        TextBlock Connection() => Descendants<TextBlock>(panel).Single(t => AutomationProperties.GetAutomationId(t) == "input-connection-98");
        Button Find(string id) => Descendants<Button>(panel).Single(b => AutomationProperties.GetAutomationId(b) == id + "-98");
        async Task Refresh() { Click(Find("input-refresh")); await Task.Delay(40); panel.UpdateLayout(); }

        panel.IsExpanded = true; await Task.Delay(40);
        panel.Measure(new Size(370, 320)); panel.Arrange(new Rect(0, 0, 370, 320)); panel.UpdateLayout();
        Check(Connection().Text == L.F("input.connection", "HDMI") && Connection().Visibility == Visibility.Visible &&
            Descendants<TextBlock>(panel).Any(t => t.Text == L.T("input.currentUnknown")) &&
            Descendants<ComboBox>(panel).Single().Items.Count == 0 && !Find("input-apply").IsEnabled && reads == 1 && writes == 0,
            "A Windows connection label is useful without DDC, without confirming or enabling an input");
        Check(Connection().ToolTip as string == L.T("input.connectionHelp"), "Connector help explains the distinction from monitor input selection");

        reported = new MonitorInputs(17, new[] { new MonitorInput(17), new MonitorInput(15) }) { ConnectionTechnology = 10 };
        await Refresh();
        Check(Connection().Text == L.F("input.connection", "DisplayPort") &&
            Descendants<TextBlock>(panel).Any(t => t.Text == L.F("input.current", new MonitorInput(17))) && !Find("input-apply").IsEnabled,
            "Refresh updates the OS connection while preserving the independent confirmed DDC input");

        foreach (uint? technology in new uint?[] { 99, null })
        {
            reported = reported with { ConnectionTechnology = technology }; await Refresh();
            Check(Connection().Visibility == Visibility.Collapsed && Connection().Text == "" && !Find("input-apply").IsEnabled,
                "Unknown or missing connector metadata clears the previous label without losing DDC state");
        }
        reported = reported with { ConnectionTechnology = 5 }; await Refresh();
        fail = true; await Refresh();
        Check(Connection().Visibility == Visibility.Collapsed && Connection().Text == "" && !Find("input-apply").IsEnabled,
            "A failed refresh clears stale connection metadata");

        fail = false; await Refresh();
        var choices = Descendants<ComboBox>(panel).Single();
        choices.SelectedItem = choices.Items.Cast<MonitorInput>().Single(i => i.Code == 15);
        Check(writes == 0 && Find("input-apply").IsEnabled, "Reading connector labels never issues a hardware write");
        Click(Find("input-apply")); await Task.Delay(40);
        Check(writes == 1 && Connection().Visibility == Visibility.Collapsed &&
            Descendants<TextBlock>(panel).Any(t => t.Text == L.T("input.currentUnknown")),
            "An unverified input switch cannot retain the old OS connection label as confirmation");
    }
}
