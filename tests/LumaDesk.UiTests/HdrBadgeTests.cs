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
    private static async Task TestHdrBadgesAsync(MainWindow window)
    {
        var savedDisplays = window.Displays;
        string? savedSelected = window.Preferences.QuickSelectedDisplayId, savedMore = window.Preferences.QuickMoreDisplayId;
        bool savedSync = window.Preferences.SyncBrightness;
        var displays = Enumerable.Range(1, 3).Select(i => new DisplayInfo
        {
            Number = i, Id = "hdr-badge-" + i, Name = "HDR test " + i,
            X = (i - 1) * 1920, Current = new(1920, 1080, 60), Primary = i == 1
        }).ToList();
        var states = new[] { new DisplayColorState(), new DisplayColorState(), new DisplayColorState() };
        AdvancedColorService.DecodeHdr(0x30, 2, states[0]); // Active HDR.
        AdvancedColorService.DecodeHdr(0xC3, 1, states[1]); // WCG SDR.
        AdvancedColorService.DecodeHdr(0x30, 0, states[2]); // Enabled, not active.
        bool fail = false;
        TaskCompletionSource<bool>? pending = null;
        int reads = 0;
        window.Displays = displays; window.Preferences.QuickSelectedDisplayId = displays[0].Id;
        window.Preferences.QuickMoreDisplayId = null; window.Preferences.SyncBrightness = true;
        var quick = new QuickWindow(window, dismissOnDeactivate: false, readHdrActive: display =>
        {
            reads++;
            if (pending != null) return pending.Task;
            if (fail) return Task.FromException<bool>(new InvalidOperationException("Probe unavailable"));
            var state = states[display.Number - 1];
            return Task.FromResult(state.HdrKnown && state.HdrActive);
        });
        Button Tile(int number) => Descendants<Button>(quick).Single(b => AutomationProperties.GetAutomationId(b) == "quick-display-select-" + number);
        Border Badge(int number) => Descendants<Border>(Tile(number)).Single(b => AutomationProperties.GetAutomationId(b) == "quick-display-select-" + number + "-hdr");
        bool Active(int number) => AutomationProperties.GetItemStatus(Tile(number)).Contains(L.T("s166778a827"));
        async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 150 && !condition(); i++) await Task.Delay(40);
            Check(condition(), "HDR badge update completes"); quick.UpdateLayout();
        }
        try
        {
            quick.Render(); quick.Show(); quick.UpdateLayout();
            await WaitFor(() => Active(1));
            Check(Badge(1).IsVisible && !Badge(2).IsVisible && !Badge(3).IsVisible,
                "Only active HDR gets a visible tile badge; WCG and enabled-only HDR do not");
            Check(AutomationProperties.GetItemStatus(Tile(1)).Contains(L.T("quick.selected")) &&
                Tile(1).ToolTip.ToString()!.Contains(L.T("s166778a827")), "HDR preserves selection status and adds a localized tooltip");
            var badgeOrigin = Badge(1).TranslatePoint(new Point(), Tile(1));
            Check(badgeOrigin.X >= 0 && badgeOrigin.Y >= 0 && badgeOrigin.X + Badge(1).ActualWidth <= Tile(1).ActualWidth &&
                badgeOrigin.Y + Badge(1).ActualHeight <= Tile(1).ActualHeight, "HDR badge fits inside its display tile");
            Snapshot(quick, "quick-hdr-badges.png");
            SelectQuick(quick, 2); await WaitFor(() => Active(1));
            Check(Badge(1).IsVisible && AutomationProperties.GetItemStatus(Tile(1)).Contains(L.T("quick.linked")),
                "Unselected linked display retains its HDR badge and linked status");
            PressQuickAction(quick, "quick-multi-toggle");
            await WaitFor(() => Active(1));
            Check(Badge(1).IsVisible && Descendants<TextBlock>(Tile(1)).Any(t => t.Text.EndsWith("%")),
                "HDR badge coexists with multi-display brightness levels");

            var stableTile = Tile(1); stableTile.Focus();
            fail = true;
            await WaitFor(() => !Active(1));
            Check(!Badge(1).IsVisible && Tile(1).ToolTip.ToString() == displays[0].Name &&
                ReferenceEquals(stableTile, Tile(1)) && stableTile.IsKeyboardFocusWithin,
                "Failed periodic probe clears stale HDR without rebuilding or losing focus");
            fail = false; states[0].HdrKnown = false;
            int before = reads;
            await WaitFor(() => reads >= before + 3);
            Check(!Active(1), "Unknown state never advertises HDR even with an active bit");
            states[0].HdrKnown = true;
            await WaitFor(() => Active(1));

            // Complete an obsolete map's read after replacing it with a new map.
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            before = reads; quick.Render(); quick.UpdateLayout();
            await WaitFor(() => reads > before);
            var obsolete = pending; pending = null; states[0].HdrActive = false;
            before = reads; quick.Render(); quick.UpdateLayout();
            await WaitFor(() => reads >= before + 3);
            obsolete.SetResult(true); await Task.Delay(80);
            Check(!Active(1), "Late result from a discarded map cannot mark its replacement HDR active");

            states[0].HdrActive = true;
            displays[0].Current = new(192, 108, 60); quick.Render(); quick.UpdateLayout();
            await WaitFor(() => Active(1));
            Check(!Badge(1).IsVisible && Tile(1).ToolTip.ToString()!.Contains(L.T("s166778a827")),
                "Tiny tiles expose HDR accessibly without overflowing a visible badge");

            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            before = reads; quick.Render(); quick.UpdateLayout();
            await WaitFor(() => reads > before);
            quick.Close(); before = reads; pending.SetResult(true); await Task.Delay(3200);
            Check(reads == before, "Closing stops polling and abandons remaining displays in an in-flight batch");
        }
        finally
        {
            pending?.TrySetResult(false); quick.Close(); window.Displays = savedDisplays;
            window.Preferences.QuickSelectedDisplayId = savedSelected; window.Preferences.QuickMoreDisplayId = savedMore;
            window.Preferences.SyncBrightness = savedSync; window.SavePreferences();
        }
    }
}
