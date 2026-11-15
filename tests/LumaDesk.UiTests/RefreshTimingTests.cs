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
    private static async Task TestRefreshTimingUiAsync(MainWindow owner)
    {
        string? previous = Environment.GetEnvironmentVariable(RefreshTimingService.Flag);
        try
        {
            int reads = 0;
            var display = new DisplayInfo { Number = 99 };
            TaskCompletionSource<RefreshTiming>? pending = null;
            bool fail = false;
            Task<RefreshTiming> Read(DisplayInfo d)
            {
                reads++;
                if (fail) throw new InvalidOperationException("Disconnected fixture");
                return pending?.Task ?? Task.FromResult(new RefreshTiming(59.94, 120, true));
            }
            Environment.SetEnvironmentVariable(RefreshTimingService.Flag, null);
            Check(owner.CreateRefreshTimingControls(display, Read) == null && reads == 0, "Timing UI defaults off without queries");
            Environment.SetEnvironmentVariable(RefreshTimingService.Flag, "1");
            var panel = (Expander)owner.CreateRefreshTimingControls(display, Read)!;
            var content = (StackPanel)panel.Content;
            var status = content.Children.OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "timing-status-99");
            var refresh = content.Children.OfType<Button>().Single();
            Check(reads == 0, "Collapsed timing inspector performs no discovery");
            panel.IsExpanded = true; await Task.Delay(20);
            Check(reads == 1 && status.Text.Contains(L.F("timing.hz", 59.94)) && status.Text.Contains(L.T("timing.on")),
                "Timing UI shows fractional rates and explicit DRR state");
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(refresh); Click(refresh);
            Check(reads == 2 && !refresh.IsEnabled && !status.Text.Contains(L.T("timing.on")), "Pending refresh clears stale state and prevents duplicate reads");
            pending.SetResult(new RefreshTiming(60, 60)); await Task.Delay(20); pending = null;
            Check(status.Text.Contains(L.T("timing.unknown")) && !status.Text.Contains(L.T("timing.off")) && refresh.IsEnabled,
                "Unavailable DRR information is distinct from disabled");
            fail = true; Click(refresh); await Task.Delay(20);
            Check(status.Text.Contains("Disconnected fixture") && !status.Text.Contains(L.F("timing.hz", 60)) && refresh.IsEnabled,
                "Failed timing query replaces stale rates and permits retry");
            fail = false; Click(refresh); await Task.Delay(20);
            Check(status.Text.Contains(L.T("timing.on")), "Refresh recovers after a disconnected display");
            Check(content.Children.OfType<TextBlock>().Any(t => t.Text == L.T("timing.help")) && content.Children.OfType<Button>().Count() == 1,
                "Inspector explains VRR limits and offers only read-only refresh");
        }
        finally { Environment.SetEnvironmentVariable(RefreshTimingService.Flag, previous); }
    }
}
