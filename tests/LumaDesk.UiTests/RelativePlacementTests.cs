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
using System.Windows.Interop;

internal static partial class Program
{
    private static async Task TestRelativePlacementFixtureAsync(MainWindow owner)
    {
        var previous = owner.Displays;
        var a = new DisplayInfo { Id = "relative-a", Number = 1, Name = "Portrait", X = -1080, Current = new(1080, 1920, 60) };
        var b = new DisplayInfo { Id = "relative-b", Number = 2, Name = "Main", Primary = true, Current = new(2560, 1440, 60) };
        var c = new DisplayInfo { Id = "relative-c", Number = 3, Name = "Other", X = 2560, Current = new(1280, 720, 60) };
        owner.Displays = new() { a, b, c };
        int calls = 0;
        IReadOnlyList<ScreenPlacement>? received = null;
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var layout = new LayoutWindow(owner, async p => { calls++; received = p; await hold.Task; });
        // Inspect the detached body; no window handles or display discovery are needed.
        var body = (StackPanel)((ScrollViewer)layout.Content).Content;
        T Find<T>(string id) where T : FrameworkElement => Descendants<T>(body).Single(e => AutomationProperties.GetAutomationId(e) == id);
        var selected = Find<ComboBox>("layout-selected");
        var neighbor = Find<ComboBox>("layout-relative-to");
        var side = Find<ComboBox>("layout-relative-side");
        var alignment = Find<ComboBox>("layout-relative-alignment");
        var place = Find<Button>("layout-relative-place");
        var apply = Find<Button>("layout-apply");
        try
        {
            Check(neighbor.Items.Cast<DisplayInfo>().SequenceEqual(new[] { b, c }) && place.IsEnabled,
                "Relative placement offers only other connected screens");
            Check(AutomationProperties.GetName(neighbor) == L.T("layout.relative.to") &&
                AutomationProperties.GetName(alignment) == L.T("layout.relative.alignment"),
                "Relative placement selectors expose localized accessibility names");
            var original = layout.Placements.ToArray();
            side.SelectedIndex = (int)LayoutSide.Above;
            Check(alignment.Items.Cast<string>().SequenceEqual(new[] { L.T("layout.relative.leftEdge"), L.T("layout.relative.center"), L.T("layout.relative.rightEdge") }),
                "Above/below placement offers horizontal edge alignment");
            alignment.SelectedIndex = (int)LayoutAlignment.End;
            Check(calls == 0 && layout.Placements.SequenceEqual(original), "Changing relative choices does not edit preview or write displays");
            Click(place);
            Check(calls == 0 && layout.Placements[0] == (original[0] with { X = 1480, Y = -1920 }) &&
                layout.Placements.Skip(1).SequenceEqual(original.Skip(1)) && a.X == -1080 && a.Y == 0,
                "Place edits only the chosen preview and leaves discovered display state unchanged");
            var coordinates = Descendants<TextBox>(body).ToArray();
            Check(coordinates[0].Text == "1480" && coordinates[1].Text == "-1920", "Relative placement refreshes pixel coordinate fields");

            neighbor.SelectedItem = c; selected.SelectedItem = b;
            Check(ReferenceEquals(neighbor.SelectedItem, c) && neighbor.Items.Cast<DisplayInfo>().SequenceEqual(new[] { a, c }),
                "Changing the selected screen preserves a still-valid neighbor and excludes self");
            selected.SelectedItem = c;
            Check(neighbor.SelectedItem is DisplayInfo next && next != c, "Selecting the old neighbor chooses a different reference screen");
            selected.SelectedItem = a; neighbor.SelectedItem = b;
            side.SelectedIndex = (int)LayoutSide.Right;
            Check(alignment.SelectedIndex == (int)LayoutAlignment.End && alignment.Items[0] as string == L.T("layout.relative.top") &&
                alignment.Items[2] as string == L.T("layout.relative.bottom"), "Side changes retain alignment choice and relabel the matching edges");
            alignment.SelectedIndex = (int)LayoutAlignment.Start;
            Click(place); Click(apply);
            Check(calls == 0 && Descendants<TextBlock>(body).Any(t => t.Text == L.T("s0947ffa0b1")),
                "A preview overlapping a third screen is rejected before the apply callback");

            side.SelectedIndex = (int)LayoutSide.Above; alignment.SelectedIndex = (int)LayoutAlignment.Center;
            Click(place); var preview = layout.Placements.ToArray(); Click(apply);
            Check(calls == 1 && received is { } applied && applied.SequenceEqual(LayoutRules.Normalize(preview)) && applied[0].X == 740 && applied[0].Y == -1920,
                "Apply submits the exact validated relative layout through the existing transaction");
            Click(place); Click(apply);
            Check(!place.IsEnabled && calls == 1 && layout.Placements.SequenceEqual(preview),
                "Pending apply blocks relative edits and duplicate submission");
            hold.SetResult(); await Task.Delay(30);
            Check(place.IsEnabled && layout.Placements.SequenceEqual(original), "Completed apply reloads actual state and re-enables relative placement");
            selected.SelectedItem = b; neighbor.SelectedItem = c;
            side.SelectedIndex = (int)LayoutSide.Above; alignment.SelectedIndex = (int)LayoutAlignment.Start; Click(place);
            selected.SelectedItem = a; neighbor.SelectedItem = b;
            side.SelectedIndex = (int)LayoutSide.Right; Click(place);
            Check(calls == 1 && layout.Placements[0].X == 5120 && layout.Placements[0].Y == -1440 &&
                layout.Placements[1].Primary && layout.Placements[2] == original[2],
                "Chained placement uses the neighbor's edited preview position, retaining primary and other screens");
            Check(new WindowInteropHelper(layout).Handle == IntPtr.Zero, "Relative fixture never creates a desktop window");
        }
        finally { hold.TrySetResult(); await Task.Delay(30); layout.Close(); owner.Displays = previous; }

        foreach (var displays in new[] { new List<DisplayInfo> { b }, new List<DisplayInfo>() })
        {
            owner.Displays = displays;
            var empty = new LayoutWindow(owner, _ => { calls++; return Task.CompletedTask; });
            try
            {
                var content = (StackPanel)((ScrollViewer)empty.Content).Content;
                var button = Descendants<Button>(content).Single(e => AutomationProperties.GetAutomationId(e) == "layout-relative-place");
                int before = calls; Click(button);
                Check(!button.IsEnabled && calls == before && empty.Placements.Count == displays.Count,
                    "Relative placement is disabled and harmless with fewer than two displays: " + displays.Count);
            }
            finally { empty.Close(); owner.Displays = previous; }
        }
    }
}
