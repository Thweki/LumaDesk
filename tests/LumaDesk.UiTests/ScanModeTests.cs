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
    private static async Task TestScanModeFixtureAsync(MainWindow owner)
    {
        string? previous = Environment.GetEnvironmentVariable(ScanModeRules.Flag);
        TaskCompletionSource<MonitorScanModes>? pending = null;
        var display = new DisplayInfo { Id = "scan-fixture", Number = 99 };
        int reads = 0, writes = 0; uint current = 2;
        bool unsupported = false, failRead = false, failWrite = false;
        MonitorScanModes State() => unsupported ? new(null, Array.Empty<MonitorScanMode>())
            : ScanModeRules.Resolve(current, "(vcp(DA(00 01 02)))");
        Task<MonitorScanModes> Read(DisplayInfo d)
        {
            Check(ReferenceEquals(d, display), "Scan query targets the selected display"); reads++;
            if (failRead) throw new InvalidOperationException("Fixture disconnected");
            return Task.FromResult(State());
        }
        async Task<MonitorScanModes> Write(DisplayInfo d, uint code)
        {
            Check(ReferenceEquals(d, display), "Scan write targets the selected display"); writes++;
            if (failWrite) throw new InvalidOperationException("Fixture write unverified");
            if (pending != null) return await pending.Task;
            current = code; return State();
        }
        try
        {
            Environment.SetEnvironmentVariable(ScanModeRules.Flag, null);
            Check(owner.CreateScanModeControls(display, Read, Write) == null && reads == 0 && writes == 0,
                "Scan controls are absent and do not probe hardware by default");
            var service = new DisplayService();
            Check((await service.ReadScanModesAsync(display)).Choices.Count == 0, "Disabled scan service does not query even an invalid native display");
            bool rejected = false;
            try { await service.SetScanModeAsync(display, 0); } catch (InvalidOperationException ex) { rejected = ex.Message == L.T("scan.disabled"); }
            Check(rejected && !service.IsWriting, "Disabled native scan write fails before any hardware access");
            Environment.SetEnvironmentVariable(ScanModeRules.Flag, "1");
            var section = (Expander)owner.CreateScanModeControls(display, Read, Write)!;
            var host = new Border { Child = section, Width = 450 };
            Check(reads == 0 && writes == 0, "Collapsed scan section performs no discovery");
            section.IsExpanded = true;
            host.Measure(new Size(450, 400)); host.Arrange(new Rect(0, 0, 450, 400)); host.UpdateLayout();
            var choice = Descendants<ComboBox>(host).Single();
            Button Button(string suffix) => Descendants<Button>(host).Single(b => AutomationProperties.GetAutomationId(b) == "scan-" + suffix + "-99");
            string Status() => Descendants<TextBlock>(host).Single(t => AutomationProperties.GetAutomationId(t) == "scan-status-99").Text;
            void Choose(uint code) => choice.SelectedItem = choice.Items.Cast<MonitorScanMode>().Single(c => c.Code == code);
            Check(reads == 1 && writes == 0 && (choice.SelectedItem as MonitorScanMode)?.Code == 2 && !Button("apply").IsEnabled,
                "Scan panel loads actual mode read-only and disables redundant apply");
            Choose(0);
            Check(writes == 0, "Choosing a scan mode is a preview until Apply");
            Click(Button("apply")); await Task.Yield();
            Check(current == 0 && writes == 1 && Status() == L.T("scan.confirmed") && !Button("apply").IsEnabled,
                "Verified scan change updates selected mode and confirmation");
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Choose(1);
            Click(Button("apply")); Click(Button("apply")); Click(Button("refresh"));
            Check(writes == 2 && reads == 1 && !choice.IsEnabled && !Button("refresh").IsEnabled,
                "Pending scan change blocks duplicate apply and refresh");
            current = 1; pending.SetResult(State()); pending = null; await Task.Delay(40);
            failWrite = true; Choose(2); Click(Button("apply")); await Task.Yield();
            Check(Status() == "Fixture write unverified" && choice.SelectedItem == null && !Button("apply").IsEnabled,
                "Unverified scan change clears stale state and requires refresh");
            failWrite = false; Click(Button("refresh")); await Task.Yield();
            Check((choice.SelectedItem as MonitorScanMode)?.Code == 1 && writes == 3, "Refresh recovers actual scan mode without writing");
            unsupported = true; Click(Button("refresh")); await Task.Yield();
            Check(choice.Items.Count == 0 && !choice.IsEnabled && !Button("apply").IsEnabled && Status() == L.T("scan.unavailable"),
                "Unsupported monitors have an explanation and no writable choices");
            failRead = true; Click(Button("refresh")); await Task.Yield();
            Check(Status() == "Fixture disconnected" && !Button("apply").IsEnabled && Button("refresh").IsEnabled,
                "Read failures remain retryable without stale write controls");
        }
        finally { pending?.TrySetResult(State()); Environment.SetEnvironmentVariable(ScanModeRules.Flag, previous); }
    }
}
