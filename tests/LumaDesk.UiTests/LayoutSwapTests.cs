// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

internal static partial class Program
{
    private static async Task TestLayoutSwapFixtureAsync(MainWindow owner)
    {
        const string flag = "LUMADESK_EXPERIMENTAL_SWAP";
        string? previousFlag = Environment.GetEnvironmentVariable(flag);
        var real = owner.Displays;
        var first = new DisplayInfo { Id = "swap-a", Number = 1, Name = "Screen A", X = -1920, Current = new(1920, 1080, 60) };
        var second = new DisplayInfo { Id = "swap-b", Number = 2, Name = "Screen B", Primary = true, Current = new(2560, 1440, 144) };
        var third = new DisplayInfo { Id = "swap-c", Number = 3, Name = "Screen C", X = 2560, Current = new(1280, 1024, 60) };
        var windows = new List<LayoutWindow>();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0; IReadOnlyList<ScreenPlacement>? received = null; bool fail = false;
        (LayoutWindow Layout, FrameworkElement Host) Create()
        {
            var layout = new LayoutWindow(owner, async p =>
            {
                calls++; received = p.ToArray(); await hold.Task;
                if (fail) throw new InvalidOperationException("Fixture layout rejected");
                foreach (var placement in p)
                {
                    var display = owner.Displays.Single(d => d.Id == placement.Id);
                    display.X = placement.X; display.Y = placement.Y;
                }
            });
            windows.Add(layout);
            // Render detached content: no desktop window, discovery or native writes.
            var host = (FrameworkElement)layout.Content; layout.Content = null;
            host.Measure(new Size(760, 700)); host.Arrange(new Rect(0, 0, 760, 700)); host.UpdateLayout();
            return (layout, host);
        }
        Button FindButton(FrameworkElement host, string id) => Descendants<Button>(host).Single(b => AutomationProperties.GetAutomationId(b) == id);
        ComboBox Combo(FrameworkElement host, string id) => Descendants<ComboBox>(host).Single(c => AutomationProperties.GetAutomationId(c) == id);
        try
        {
            owner.Displays = new() { first, second, third };
            foreach (string? disabled in new string?[] { null, "0", "true" })
            {
                Environment.SetEnvironmentVariable(flag, disabled);
                var hidden = Create();
                Check(!Descendants<Button>(hidden.Host).Any(b => AutomationProperties.GetAutomationId(b) == "layout-swap"),
                    "Experimental swap is hidden unless its flag is exactly 1: " + (disabled ?? "unset"));
            }
            Environment.SetEnvironmentVariable(flag, "1");
            var (layout, host) = Create();
            var swap = FindButton(host, "layout-swap"); var apply = FindButton(host, "layout-apply");
            var target = Combo(host, "layout-swap-target"); var selected = Combo(host, "layout-selected");
            Check(target.Items.Count == 2 && (string?)target.SelectedValue == second.Id && swap.IsEnabled,
                "Swap offers all other connected displays and excludes the selected source");
            selected.SelectedItem = second;
            Check(target.Items.Count == 2 && (string?)target.SelectedValue == first.Id, "Changing source rebuilds targets without offering itself");
            selected.SelectedItem = first; target.SelectedValue = second.Id;
            var original = layout.Placements.ToArray();
            Click(swap);
            Check(calls == 0 && first.X == -1920 && second.X == 0 && layout.Placements[0].X == 2560 &&
                layout.Placements[1].Primary && layout.Placements[1].X == 0 && layout.Placements[2].X == 4480,
                "Swap updates only the preview, retaining primary status and other relative positions");
            Check(Descendants<TextBlock>(host).Any(t => t.Text == L.T("layout.swap.preview")), "Preview explicitly asks the user to apply");
            Click(swap);
            Check(layout.Placements.SequenceEqual(original) && calls == 0, "Second preview swap restores the original arrangement without writes");
            Click(swap); var preview = layout.Placements.ToArray(); Click(apply);
            Check(calls == 1 && received!.SequenceEqual(preview) && !swap.IsEnabled && !target.IsEnabled,
                "Apply sends the full normalized preview through the existing transaction and disables swapping");
            Click(swap); Click(apply);
            Check(calls == 1 && layout.Placements.SequenceEqual(preview), "Pending apply rejects duplicate apply and preview edits");
            hold.SetResult(); await Task.Delay(40);
            Check(swap.IsEnabled && layout.Placements.SequenceEqual(preview), "Successful apply reloads actual positions and re-enables swapping");
            Click(swap); var retry = layout.Placements.ToArray(); fail = true; Click(apply); await Task.Delay(40);
            Check(calls == 2 && swap.IsEnabled && layout.Placements.SequenceEqual(retry) &&
                Descendants<TextBlock>(host).Any(t => t.Text == "Fixture layout rejected"), "Failed apply preserves the swapped preview and reports a retryable error");
            fail = false; Click(apply); await Task.Delay(40);
            Check(calls == 3 && layout.Placements.SequenceEqual(original), "Retry applies the same preview successfully");

            // Unequal non-neighbors cannot exchange origins when the middle screen would overlap.
            first.X = 0; first.Primary = true; first.Current = new(100, 100, 60);
            second.X = 100; second.Primary = false; second.Current = new(100, 100, 60);
            third.X = 200; third.Current = new(200, 100, 60);
            var invalid = Create(); var intact = invalid.Layout.Placements.ToArray();
            Combo(invalid.Host, "layout-swap-target").SelectedValue = third.Id;
            Click(FindButton(invalid.Host, "layout-swap"));
            Check(invalid.Layout.Placements.SequenceEqual(intact) && calls == 3 &&
                Descendants<TextBlock>(invalid.Host).Any(t => t.Text == L.T("s0947ffa0b1")), "Invalid swap leaves the preview intact and explains the overlap without writing");
            owner.Displays = new() { first };
            var single = Create();
            Check(!FindButton(single.Host, "layout-swap").IsEnabled && Combo(single.Host, "layout-swap-target").Items.Count == 0,
                "Single-screen setup disables swap");
            owner.Displays.Clear(); var empty = Create();
            Check(!FindButton(empty.Host, "layout-swap").IsEnabled, "Empty setup disables swap");
        }
        finally
        {
            hold.TrySetResult(); await Task.Delay(20);
            foreach (var layout in windows) layout.Close();
            owner.Displays = real; Environment.SetEnvironmentVariable(flag, previousFlag);
        }
    }
}
