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
    private static void SelectQuick(QuickWindow quick, int number)
    {
        Click(Descendants<Button>(quick).Single(b => AutomationProperties.GetAutomationId(b) == "quick-display-select-" + number));
        quick.UpdateLayout();
    }

    private static async Task TestQuickDisplayPickerAsync(MainWindow window)
    {
        var real = window.Displays;
        string? savedSelected = window.Preferences.QuickSelectedDisplayId, savedMore = window.Preferences.QuickMoreDisplayId;
        bool savedSync = window.Preferences.SyncBrightness;
        var first = new DisplayInfo { Number = 1, Id = "picker-a", Name = "测试屏幕 Z", X = -2560, Y = 0, Current = new(2560, 1440, 144), DpiScale = DpiRules.FromRelative(0, 0, 3), Brightness = 65 };
        var second = new DisplayInfo { Number = 2, Id = "picker-b", Name = "测试屏幕 B", Primary = true, X = 0, Y = 0, Current = new(3840, 2160, 60), DpiScale = DpiRules.FromRelative(0, 1, 2), Brightness = 80 };
        var third = new DisplayInfo { Number = 3, Id = "picker-c", Name = "测试屏幕 C", X = 3840, Y = -400, Current = new(1440, 2560, 60), DpiScale = DpiRules.FromRelative(0, 0, 2), Brightness = 45 };
        var displays = new[] { first, second, third };
        TaskCompletionSource<bool>? pending = null;
        DisplayInfo? requested = null; int requestedValue = 0;
        QuickWindow Create() => new(window, (_, _, _) => Task.CompletedTask, dismissOnDeactivate: false,
            applyBrightness: async (display, value) => { requested = display; requestedValue = value; if (pending != null) await pending.Task; display.Brightness = value; });
        window.Displays = displays.ToList(); window.Preferences.QuickSelectedDisplayId = second.Id;
        window.Preferences.QuickMoreDisplayId = null; window.Preferences.SyncBrightness = false; window.Hide();
        QuickWindow quick = Create();
        bool Has(string id) => Descendants<FrameworkElement>(quick).Any(e => AutomationProperties.GetAutomationId(e) == id);
        Button Find(string id) => Descendants<Button>(quick).Single(b => AutomationProperties.GetAutomationId(b) == id);
        try
        {
            var tiles = QuickDisplayMap.Project(displays, 320, 116);
            var a = tiles.Single(t => t.Display == first).Bounds; var b = tiles.Single(t => t.Display == second).Bounds; var c = tiles.Single(t => t.Display == third).Bounds;
            Check(a.Right < b.Left && b.Right < c.Left && c.Top < b.Top && c.Height > c.Width && a.Width > a.Height,
                "Map retains negative coordinates, vertical offsets and portrait orientation");
            Check(tiles.All(t => t.Bounds.Left >= 0 && t.Bounds.Top >= 0 && t.Bounds.Right <= 320 && t.Bounds.Bottom <= 116), "Screen map fits its available area");
            var clone = new DisplayInfo { Id = "clone", X = first.X, Y = first.Y, Current = first.Current };
            var cloneTiles = QuickDisplayMap.Project(new[] { first, clone }, 160, 100);
            Check(cloneTiles.Count == 2 && !cloneTiles[0].Bounds.IntersectsWith(cloneTiles[1].Bounds) && cloneTiles.All(t => t.Bounds.Width > t.Bounds.Height), "Mirrored screens remain individually selectable and retain their orientation");
            Check(QuickDisplayMap.Project(Array.Empty<DisplayInfo>(), 320, 116).Count == 0, "Empty topology produces no invalid map bounds");

            quick.Render(); quick.Show(); quick.UpdateLayout();
            Check(Has("quick-display-select-1") && Has("quick-display-select-2") && Has("quick-display-select-3"), "Every screen has a layout selection target");
            Check(Has("quick-dpi-slider-2") && !Has("quick-dpi-slider-1") && !Has("quick-dpi-slider-3"), "Only the chosen screen exposes adjustment controls");
            Check(!Descendants<TextBlock>(quick).Any(t => displays.Any(d => t.Text == d.Name) || new[] { "左侧", "中间", "右侧", "1", "2", "3" }.Contains(t.Text)), "Picker omits visible names, numbers and positional text");
            Check(AutomationProperties.GetItemStatus(Find("quick-display-select-2")) == L.T("quick.selected"), "Selected screen is exposed to accessibility tools");
            Snapshot(quick, "quick-picker-three-screens.png");
            Click(Find("quick-more-toggle-2")); quick.UpdateLayout();
            Check(Has("quick-more-content-2") && Descendants<TextBlock>(quick).Any(t => t.Text == "画中画"), "More settings retains existing advanced functions");
            var moreButton = Find("quick-more-toggle-2");
            var moreContent = Descendants<FrameworkElement>(quick).Single(e => AutomationProperties.GetAutomationId(e) == "quick-more-content-2");
            Check(moreButton.TranslatePoint(new Point(), quick).Y < moreContent.TranslatePoint(new Point(), quick).Y, "Disclosure button remains above its expanded controls");
            SelectQuick(quick, 1);
            Check(quick.SelectedDisplayId == first.Id && Has("quick-more-content-1") && !Has("quick-more-content-2"), "Expanded controls follow the selected screen");
            Check(Storage.Load().QuickSelectedDisplayId == first.Id && Storage.Load().QuickMoreDisplayId == first.Id, "Selection persists by stable device ID");
            quick.Close(); quick = Create(); quick.Render(); quick.Show(); quick.UpdateLayout();
            Check(quick.SelectedDisplayId == first.Id && Has("quick-more-content-1"), "Selected screen and disclosure survive reopening");
            first.Name = "重命名后的屏幕"; first.Number = 9; window.Displays.Reverse(); quick.Render(); quick.UpdateLayout();
            Check(quick.SelectedDisplayId == first.Id && Has("quick-dpi-slider-9"), "Rename, numbering and enumeration order do not retarget controls");
            SelectQuick(quick, 2); Click(Find("quick-more-toggle-2")); quick.UpdateLayout();
            Check(!Has("quick-more-content-2") && Storage.Load().QuickMoreDisplayId == null, "More settings collapses and persists");

            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var slider = Descendants<Slider>(quick).Single(s => AutomationProperties.GetName(s) == "亮度");
            slider.Value = 37; await Task.Delay(220);
            SelectQuick(quick, 3);
            Check(ReferenceEquals(requested, second) && requestedValue == 37 && quick.SelectedDisplayId == second.Id,
                "Pending brightness targets the selected identity and prevents a screen switch");
            pending.SetResult(true); pending = null; await Task.Delay(180);
            SelectQuick(quick, 3);
            Check(quick.SelectedDisplayId == third.Id && Has("quick-dpi-slider-3"), "Target switching resumes after brightness finishes");
            slider = Descendants<Slider>(quick).Single(s => AutomationProperties.GetName(s) == "亮度");
            slider.Value = 52; await Task.Delay(220);
            Check(ReferenceEquals(requested, third) && requestedValue == 52 && second.Brightness == 37, "Controls after switching bind to the new screen only");

            window.Preferences.SyncBrightness = true; quick.Render(); quick.UpdateLayout();
            Check(Has("quick-brightness-scope") && AutomationProperties.GetItemStatus(Find("quick-display-select-2")) == L.T("quick.linked"), "Synchronized brightness shows all affected targets");
            window.Preferences.SyncBrightness = false;
            Click(Find("quick-more-toggle-3")); quick.UpdateLayout();
            Snapshot(quick, "quick-picker-more.png");
            var map = Descendants<Canvas>(quick).Single(e => AutomationProperties.GetAutomationId(e) == "quick-display-map");
            double mapTop = map.TranslatePoint(new Point(), quick).Y;
            Descendants<ScrollViewer>(quick).First().ScrollToBottom(); quick.UpdateLayout();
            Check(Math.Abs(mapTop - map.TranslatePoint(new Point(), quick).Y) < 0.1, "Screen identity stays visible while advanced controls scroll");
            ThemeService.Apply("dark"); Snapshot(quick, "quick-picker-dark.png"); ThemeService.Apply("light");
            window.Displays.Remove(third); quick.Render(); quick.UpdateLayout();
            Check(quick.SelectedDisplayId == second.Id && Has("quick-more-content-2") && !Has("quick-display-select-3"), "Unplugging selected screen falls back to a connected screen with disclosure preserved");
            window.Displays = new() { first }; quick.Render(); quick.UpdateLayout();
            Check(quick.SelectedDisplayId == first.Id && Has("quick-dpi-slider-9"), "Single remaining screen remains usable");
            window.Displays.Clear(); quick.Render(); quick.UpdateLayout();
            Check(quick.SelectedDisplayId == null && !Descendants<Slider>(quick).Any() && !Has("quick-identify"), "No screens leaves no stale controls or locate action");
            window.Displays = new() { third }; quick.Render(); quick.UpdateLayout();
            Check(quick.SelectedDisplayId == third.Id && Has("quick-dpi-slider-3"), "Controls recover after screens reconnect");
            Check(!window.IsVisible, "Screen selection and disclosure keep full settings closed");

            quick.Close(); window.Displays = real; window.Preferences.QuickSelectedDisplayId = real[0].Id;
            window.Preferences.QuickMoreDisplayId = null;
            quick = Create(); quick.Render(); quick.Show(); quick.Activate(); quick.UpdateLayout();
            Click(Find("quick-identify")); await Task.Delay(100);
            var overlay = Application.Current.Windows.OfType<DisplayIdentifyWindow>().Single();
            var handle = new WindowInteropHelper(overlay).Handle;
            Native.GetWindowRect(handle, out var overlayBounds);
            var target = real[0];
            Check(overlayBounds.Left == target.X && overlayBounds.Top == target.Y && overlayBounds.Right - overlayBounds.Left == target.Current.Width && overlayBounds.Bottom - overlayBounds.Top == target.Current.Height,
                "Locate overlay uses the selected display's physical pixel bounds");
            Check((Native.GetWindowLong(handle, -20) & 0x8000020) == 0x8000020 && quick.IsVisible && !overlay.IsActive,
                "Locate overlay passes clicks through and never activates");
            await Task.Delay(1400);
            Check(!Application.Current.Windows.OfType<DisplayIdentifyWindow>().Any() && quick.IsVisible, "Locate overlay expires without closing the panel");
            Click(Find("quick-identify")); await Task.Delay(50); quick.Close();
            Check(!Application.Current.Windows.OfType<DisplayIdentifyWindow>().Any(), "Closing panel also closes its locate overlay");
        }
        finally
        {
            pending?.TrySetResult(true); await Task.Delay(80); quick.Close();
            window.Displays = real; window.Preferences.QuickSelectedDisplayId = savedSelected;
            window.Preferences.QuickMoreDisplayId = savedMore; window.Preferences.SyncBrightness = savedSync;
            window.SavePreferences(); ThemeService.Apply("light"); window.Show();
        }
    }
}
