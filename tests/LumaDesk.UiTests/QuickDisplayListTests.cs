// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

internal static partial class Program
{
    private static async Task TestQuickDisplayListAsync(MainWindow owner)
    {
        var originalDisplays = owner.Displays;
        var originalOptions = owner.Preferences.DisplayOptions;
        var originalGroups = owner.Preferences.Groups;
        bool originalList = owner.Preferences.QuickDisplayList, originalSync = owner.Preferences.SyncBrightness;
        string? originalSelected = owner.Preferences.QuickSelectedDisplayId, originalMore = owner.Preferences.QuickMoreDisplayId;
        var first = new DisplayInfo { Id = "list-a", Number = 1, Name = "Desk", X = -1920, Current = new(1920, 1080, 60), Brightness = 50 };
        var second = new DisplayInfo { Id = "list-b", Number = 2, Name = "Desk", Primary = true, Current = new(1920, 1080, 60), Brightness = 60 };
        var third = new DisplayInfo { Id = "list-c", Number = 3, Name = "Portrait", X = 1920, Y = -400, Current = new(1080, 1920, 60), Brightness = 70 };
        var displays = new[] { first, second, third };
        var topology = displays.Select(d => (d.Id, d.X, d.Y, d.Primary, d.Current)).ToArray();
        TaskCompletionSource<bool>? pending = null;
        int writes = 0;
        DisplayInfo? target = null;
        QuickWindow Create() => new(owner, dismissOnDeactivate: false, applyBrightness: async (d, value) =>
        {
            writes++; target = d;
            if (pending != null) await pending.Task;
            d.Brightness = value;
        });
        owner.Displays = displays.ToList(); owner.Hide();
        owner.Preferences.DisplayOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            [third.Id] = new() { Order = 0 }, [first.Id] = new() { Order = 1 }, [second.Id] = new() { Order = 2 }
        };
        owner.Preferences.Groups = new(); owner.Preferences.SyncBrightness = false;
        owner.Preferences.QuickDisplayList = false; owner.Preferences.QuickSelectedDisplayId = second.Id;
        owner.Preferences.QuickMoreDisplayId = null;
        QuickWindow quick = Create();
        bool Has(string id) => Descendants<FrameworkElement>(quick).Any(e => AutomationProperties.GetAutomationId(e) == id);
        Button Find(string id) => Descendants<Button>(quick).Single(b => AutomationProperties.GetAutomationId(b) == id);
        Button[] Rows() => Descendants<Button>(quick).Where(b => AutomationProperties.GetAutomationId(b).StartsWith("quick-display-select-")).ToArray();
        void ToggleView() { Click(Find("quick-display-view")); quick.UpdateLayout(); }
        void Arrow(Button button, Key key)
        {
            button.Focus();
            button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(quick)!, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
            quick.UpdateLayout();
        }
        try
        {
            quick.Render(); quick.Show(); quick.UpdateLayout();
            Check(Has("quick-display-map") && !Has("quick-display-list"), "Default tray picker is the spatial map");
            ToggleView();
            Check(Has("quick-display-list") && !Has("quick-display-map") && Storage.Load().QuickDisplayList,
                "View toggle renders and persists the named list");
            Check(Rows().Select(b => b.Tag as string).SequenceEqual(new[] { third.Id, first.Id, second.Id }),
                "Tray list follows saved panel order instead of coordinates, names or discovery order");
            Check(displays.All(d => Descendants<TextBlock>(Find("quick-display-select-" + d.Number)).Any(t => t.Text == d.Number + " · " + d.Name)),
                "Rows show names and numbers to distinguish identically named monitors");
            Check(displays.All(d => AutomationProperties.GetName(Find("quick-display-select-" + d.Number)) == d.Number + " · " + d.Name),
                "Accessible row names also distinguish identically named monitors");
            Check(quick.SelectedDisplayId == second.Id && AutomationProperties.GetItemStatus(Find("quick-display-select-2")) == L.T("quick.selected"),
                "Changing view preserves selected identity and accessible selection state");
            Arrow(Find("quick-display-select-2"), Key.Down);
            Check(quick.SelectedDisplayId == third.Id && Find("quick-display-select-3").IsKeyboardFocusWithin,
                "Arrow navigation wraps in saved order and restores focus after rebuilding");
            SelectQuick(quick, 1);
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(Find("quick-brightness-up")); await Task.Delay(100);
            ToggleView(); SelectQuick(quick, 2);
            Check(writes == 1 && target == first && Has("quick-display-list") && quick.SelectedDisplayId == first.Id,
                "List brightness targets the selected identity and blocks view or target changes while writing");
            pending.SetResult(true); pending = null; await Task.Delay(100);
            ToggleView();
            Check(Has("quick-display-map") && quick.SelectedDisplayId == first.Id && !Storage.Load().QuickDisplayList,
                "Returning to the map preserves selection and persists the view");
            ToggleView();
            quick.Close(); quick = Create(); quick.Render(); quick.Show(); quick.UpdateLayout();
            Check(Has("quick-display-list") && quick.SelectedDisplayId == first.Id, "Named list and selected identity survive reopening");
            owner.Displays.Reverse(); first.Name = "Renamed desk"; first.Number = 9; quick.Render(); quick.UpdateLayout();
            Check(Rows().Select(b => b.Tag as string).SequenceEqual(new[] { third.Id, first.Id, second.Id }) && quick.SelectedDisplayId == first.Id,
                "List ordering and selection survive renumbering, alias changes and rediscovery");
            owner.Preferences.SyncBrightness = true; quick.Render(); quick.UpdateLayout();
            Check(AutomationProperties.GetItemStatus(Find("quick-display-select-2")) == L.T("quick.linked"), "List exposes linked brightness targets");
            owner.Preferences.SyncBrightness = false;
            Click(Find("quick-multi-toggle")); quick.UpdateLayout(); SelectQuick(quick, 3);
            Check(quick.SelectedBrightnessIds.Count == 2 && quick.SelectedBrightnessIds.Contains(first.Id) && quick.SelectedBrightnessIds.Contains(third.Id),
                "Named list supports selecting multiple brightness targets");
            ToggleView(); ToggleView();
            Check(quick.SelectedBrightnessIds.Count == 2 && AutomationProperties.GetItemStatus(Find("quick-display-select-3")) == L.T("quick.selected"),
                "Switching views preserves multi-selection and accessible state");
            Arrow(Find("quick-display-select-3"), Key.Down);
            Check(Find("quick-display-select-9").IsKeyboardFocusWithin && quick.SelectedBrightnessIds.Count == 2,
                "Multi-select arrow navigation moves focus without changing targets");
            Click(Find("quick-multi-toggle")); quick.UpdateLayout();
            Snapshot(quick, "quick-display-list.png");
            owner.Displays.Remove(first); quick.Render(); quick.UpdateLayout();
            Check(!Has("quick-display-select-9") && quick.SelectedDisplayId == second.Id, "Disconnected list selection falls back to a connected display");
            owner.Displays.Add(first); quick.Render(); quick.UpdateLayout();
            Check(Rows().Select(b => b.Tag as string).SequenceEqual(new[] { third.Id, first.Id, second.Id }), "Reconnected display returns to its saved list position");
            Check(displays.Select(d => (d.Id, d.X, d.Y, d.Primary, d.Current)).SequenceEqual(topology) && writes == 1,
                "List selection and view changes preserve physical topology and never trigger hardware adjustments");
            owner.Displays = Enumerable.Range(10, 8).Select(n => new DisplayInfo { Id = "list-many-" + n, Number = n, Name = "Display " + n }).ToList();
            owner.Preferences.QuickSelectedDisplayId = owner.Displays[0].Id;
            quick.Render(); quick.UpdateLayout();
            var listScroll = Descendants<ScrollViewer>(quick).Single(s => s.Content is StackPanel panel && AutomationProperties.GetAutomationId(panel) == "quick-display-list");
            Check(Rows().Length == 8 && listScroll.ActualHeight <= 150 && listScroll.ScrollableHeight > 0,
                "Large display lists scroll within a bounded picker instead of hiding adjustment controls");
            SelectQuick(quick, 17);
            Check(quick.SelectedDisplayId == "list-many-17" && Find("quick-display-select-17").IsKeyboardFocusWithin,
                "Scrolled list rows remain selectable and receive keyboard focus");
            owner.Displays = new() { second }; quick.Render(); quick.UpdateLayout();
            Check(Rows().Length == 1 && quick.SelectedDisplayId == second.Id, "Named list supports a single display");
            owner.Displays.Clear(); quick.Render(); quick.UpdateLayout();
            Check(Rows().Length == 0 && !Has("quick-display-view"), "Empty topology leaves no stale list rows or view toggle");
        }
        finally
        {
            pending?.TrySetResult(true);
            quick.Close(); owner.Displays = originalDisplays;
            owner.Preferences.DisplayOptions = originalOptions; owner.Preferences.Groups = originalGroups;
            owner.Preferences.QuickDisplayList = originalList; owner.Preferences.SyncBrightness = originalSync;
            owner.Preferences.QuickSelectedDisplayId = originalSelected; owner.Preferences.QuickMoreDisplayId = originalMore;
            Storage.Save(owner.Preferences);
        }
    }
}
