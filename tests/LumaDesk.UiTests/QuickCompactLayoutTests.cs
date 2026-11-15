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
    private static async Task TestQuickCompactLayoutAsync(MainWindow owner, bool offscreen = true)
    {
        string? flag = Environment.GetEnvironmentVariable(QuickCompactLayout.Flag);
        var real = owner.Displays;
        string? selected = owner.Preferences.QuickSelectedDisplayId, more = owner.Preferences.QuickMoreDisplayId;
        bool sync = owner.Preferences.SyncBrightness;
        var displays = Enumerable.Range(1, 9).Select(n => new DisplayInfo
        {
            Id = "compact-fixture-" + n, Number = n, Name = "Display " + n + " with a long descriptive name",
            X = (n - 1) * 1920, Current = new(1920, 1080, 60), Brightness = 50
        }).ToList();
        owner.Displays = displays;
        owner.Preferences.QuickSelectedDisplayId = displays[0].Id;
        owner.Preferences.QuickMoreDisplayId = null; owner.Preferences.SyncBrightness = false;
        QuickWindow? quick = null;
        FrameworkElement root = null!;
        TaskCompletionSource<bool>? pending = null;
        string? writtenId = null;
        void Layout()
        {
            if (offscreen) { root.Measure(new Size(358, 620)); root.Arrange(new Rect(0, 0, 358, 620)); }
            root.UpdateLayout();
        }
        bool Has(string id) => Descendants<FrameworkElement>(root).Any(e => AutomationProperties.GetAutomationId(e) == id);
        Button Find(string id) => Descendants<Button>(root).Single(b => AutomationProperties.GetAutomationId(b) == id);
        void Press(string id) { Click(Find(id)); Layout(); }
        void Create()
        {
            quick = new QuickWindow(owner, dismissOnDeactivate: false, applyBrightness: async (display, value) =>
            {
                writtenId = display.Id;
                if (pending != null) await pending.Task;
                display.Brightness = value;
            }, applySelectedBrightness: (ids, value) =>
            {
                foreach (var display in displays.Where(d => ids.Contains(d.Id))) display.Brightness = value;
                return Task.CompletedTask;
            });
            quick.Render(); root = (FrameworkElement)quick.Content;
            if (!offscreen) quick.Show();
            Layout();
        }
        try
        {
            foreach (string? value in new string?[] { null, "0", "true", "invalid" })
            {
                Environment.SetEnvironmentVariable(QuickCompactLayout.Flag, value); Create();
                Check(Has("quick-display-map") && !Has("quick-layout-toggle") && !Has("quick-display-compact"),
                    "Compact picker requires explicit flag value 1: " + (value ?? "unset"));
                quick!.Close();
            }
            Environment.SetEnvironmentVariable(QuickCompactLayout.Flag, "1"); Create();
            Check(Has("quick-display-map") && Has("quick-layout-toggle"), "Opt-in exposes a layout switch without replacing the initial map");
            Press("quick-layout-toggle");
            Check(!Has("quick-display-map") && Has("quick-display-compact"), "Compact switch replaces the spatial picker");
            var scroll = Descendants<ScrollViewer>(root).Single(s => AutomationProperties.GetAutomationId(s) == "quick-display-compact-scroll");
            Check(scroll.ActualHeight <= 108 && scroll.ScrollableHeight > 0, "Nine named displays have a bounded three-row viewport and overflow scroll");
            Check(Descendants<Button>(root).Count(b => AutomationProperties.GetAutomationId(b).StartsWith("quick-display-select-")) == 9,
                "Every connected display remains selectable beyond the viewport");
            scroll.ScrollToBottom(); Layout();
            var last = Find("quick-display-select-9");
            double lastTop = last.TranslatePoint(new Point(), scroll).Y;
            Check(scroll.VerticalOffset > 0 && lastTop >= 0 && lastTop + last.ActualHeight <= scroll.ActualHeight,
                "Scrolling makes the final display fully visible without enlarging the picker");
            var first = Find("quick-display-select-1");
            Check(first.Height >= 32 && first.ToolTip as string == displays[0].Name &&
                AutomationProperties.GetName(first) == displays[0].Name &&
                Descendants<TextBlock>(first).Any(t => t.Text.Contains(displays[0].Name) && t.TextTrimming == TextTrimming.CharacterEllipsis),
                "Compact tiles retain readable hit targets, trimmed names and full accessible names");

            Press("quick-display-select-9");
            Check(quick!.SelectedDisplayId == displays[8].Id && AutomationProperties.GetItemStatus(Find("quick-display-select-9")) == L.T("quick.selected"),
                "Compact selection binds by stable identity and exposes selection status");
            if (!offscreen)
            {
                Check(Find("quick-display-select-9").IsKeyboardFocusWithin, "Selection restores keyboard focus after rebuilding the scrollable picker");
                var button = Find("quick-display-select-9");
                button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(quick), 0, Key.Up) { RoutedEvent = Keyboard.KeyDownEvent });
                Layout();
                Check(quick.SelectedDisplayId == displays[6].Id, "Up moves a full row in the compact grid");
                Snapshot(quick, "quick-compact-nine-displays.png");
            }
            Press("quick-display-select-9");
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Press("quick-brightness-down"); await Task.Delay(100);
            Press("quick-layout-toggle"); Press("quick-display-select-1");
            Check(writtenId == displays[8].Id && quick.SelectedDisplayId == displays[8].Id && Has("quick-display-compact"),
                "Pending brightness writes block both layout and target switches");
            pending.SetResult(true); pending = null; await Task.Delay(100);
            Press("quick-layout-toggle");
            Check(Has("quick-display-map") && quick.SelectedDisplayId == displays[8].Id, "Returning to the map preserves the adjusted target");
            Press("quick-layout-toggle"); Press("quick-multi-toggle"); Press("quick-display-select-1");
            var expected = new[] { displays[0].Id, displays[8].Id };
            Check(quick.SelectedBrightnessIds.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)), "Compact tiles support explicit multiple-display selection");
            Press("quick-layout-toggle"); Press("quick-layout-toggle");
            Check(quick.SelectedBrightnessIds.OrderBy(x => x).SequenceEqual(expected.OrderBy(x => x)), "Switching layouts preserves the entire brightness selection");
            var slider = Descendants<Slider>(root).Single(s => AutomationProperties.GetAutomationId(s) == "quick-multi-brightness");
            slider.Value = 35; await Task.Delay(150); Layout();
            Check(displays[0].Brightness == 35 && displays[8].Brightness == 35 && displays.Skip(1).Take(7).All(d => d.Brightness == 50),
                "Compact multi-brightness changes exactly the selected identities");
            Check(Descendants<TextBlock>(Find("quick-display-select-9")).Any(t => t.Text == "35%"), "Compact percentages track completed multi-brightness writes");
            // Only remove an unselected display: this fixture never invokes native fallback discovery.
            owner.Displays = displays.Where(d => d.Number != 2).Reverse().ToList(); quick.Render(); Layout();
            Check(!Has("quick-display-select-2") && quick.SelectedBrightnessIds.Count == 2, "Reordering and unplugging an unselected display do not retarget selection");
            quick.Close(); Create();
            Check(Has("quick-display-map") && !Has("quick-display-compact"), "Layout choice is session-only and does not change saved settings");

            foreach (int count in new[] { 0, 1, 2, 3, 6, 9 })
            {
                var tiles = QuickCompactLayout.Project(displays.Take(count).ToArray(), 300);
                Check(tiles.Count == count && tiles.All(t => t.Bounds.Left >= 0 && t.Bounds.Right <= 300 && t.Bounds.Bottom <= QuickCompactLayout.Height(count)),
                    "Compact projection fits each display count: " + count);
                Check(!tiles.SelectMany((a, i) => tiles.Skip(i + 1).Select(b => a.Bounds.IntersectsWith(b.Bounds))).Any(overlap => overlap),
                    "Compact tiles never overlap: " + count);
            }
            Check(QuickCompactLayout.Height(6) == 108 && QuickCompactLayout.Height(4) == 72 && QuickCompactLayout.Project(displays, 0).Count == 0,
                "Six displays fit below the map's 116-DIP height and invalid width yields no tiles");
        }
        finally
        {
            pending?.TrySetResult(true); await Task.Delay(100); quick?.Close();
            owner.Displays = real; owner.Preferences.QuickSelectedDisplayId = selected;
            owner.Preferences.QuickMoreDisplayId = more; owner.Preferences.SyncBrightness = sync;
            foreach (var display in displays) owner.Preferences.DisplayOptions.Remove(display.Id);
            owner.SavePreferences(); Environment.SetEnvironmentVariable(QuickCompactLayout.Flag, flag);
        }
    }
}
