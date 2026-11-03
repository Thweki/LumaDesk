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
    private static async Task TestInputSwitchUiAsync(MainWindow window)
    {
        var display = new DisplayInfo { Id = "input-fixture", Number = 99, Name = "Input fixture" };
        uint current = 17; int writes = 0, reads = 0; bool fail = false, unverified = false;
        TaskCompletionSource<bool>? hold = null;
        var input = (Expander)window.CreateInputControls(display,
            _ => { reads++; return Task.FromResult(new MonitorInputs(current, new[] { new MonitorInput(17), new MonitorInput(15) })); },
            async (_, code) =>
            {
                writes++; if (hold != null) await hold.Task;
                if (fail) throw new InvalidOperationException("Fixture input ignored");
                if (unverified) return new InputSwitchResult(false, null);
                current = code; return new InputSwitchResult(true, code);
            });
        var host = new Window { Width = 370, Height = 290, Content = new Border { Padding = new Thickness(15), Child = input }, ShowInTaskbar = false };
        ComboBox Choice() => Descendants<ComboBox>(host).Single();
        Button Find(string id) => Descendants<Button>(host).Single(b => AutomationProperties.GetAutomationId(b) == id);
        string Status() => Descendants<TextBlock>(host).Single(t => AutomationProperties.GetAutomationId(t) == "input-status-99").Text;
        void Choose(uint code) => Choice().SelectedItem = Choice().Items.Cast<MonitorInput>().Single(i => i.Code == code);
        try
        {
            host.Show(); input.IsExpanded = true; await Task.Delay(60); host.UpdateLayout();
            Check(reads == 1 && writes == 0 && (Choice().SelectedItem as MonitorInput)?.Code == 17 && !Find("input-apply-99").IsEnabled,
                "Input panel reads the current input without issuing a command");
            Choose(15); Click(Find("input-apply-99")); await Task.Delay(60);
            Check(writes == 1 && current == 15 && Status() == L.F("input.confirmed", new MonitorInput(15)), "Input UI reports confirmation only after verified readback");
            Choose(17); Click(Find("input-apply-99")); await Task.Delay(60);
            Check(writes == 2 && current == 17, "Switching back to the original input is not blocked by stale captured state");
            hold = new(TaskCreationOptions.RunContinuationsAsynchronously); Choose(15);
            Click(Find("input-apply-99")); Click(Find("input-apply-99")); Click(Find("input-refresh-99"));
            Check(writes == 3 && reads == 1 && !Choice().IsEnabled && !Find("input-apply-99").IsEnabled, "Pending input change prevents duplicate writes and refresh races");
            hold.SetResult(true); hold = null; await Task.Delay(60);
            fail = true; Choose(17); Click(Find("input-apply-99")); await Task.Delay(60);
            Check(writes == 4 && current == 15 && Status().Contains("Fixture input ignored") && (Choice().SelectedItem as MonitorInput)?.Code == 15,
                "Ignored input writes show failure and reload the actual current input");
            fail = false; unverified = true; Choose(17); Click(Find("input-apply-99")); await Task.Delay(60);
            Check(Status() == L.T("input.unverified") && Descendants<TextBlock>(host).Any(t => t.Text == L.T("input.currentUnknown")), "An unreadable post-switch connection is not presented as confirmed success");
            unverified = false; Click(Find("input-refresh-99")); await Task.Delay(40);
            Click(Find("input-refresh-99")); await Task.Delay(40); host.UpdateLayout();
            Check((Choice().SelectedItem as MonitorInput)?.Code == 15 && !Find("input-apply-99").IsEnabled && writes == 5,
                "Repeated read-only refresh restores current state without writes or duplicate-parent UI errors");
            Snapshot(host, "input-switch-confirmation.png");
        }
        finally { hold?.TrySetResult(true); await Task.Delay(40); host.Close(); }
    }
}
