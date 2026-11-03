// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumaDesk;

internal sealed partial class QuickWindow
{
    private string? selectedDisplayId;
    private readonly List<DisplayIdentifyWindow> identifyWindows = new();
    private bool brightnessMultiSelect;
    private readonly HashSet<string> multiSelection = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> multiLevelLabels = new(StringComparer.OrdinalIgnoreCase);
    internal bool IsBrightnessMultiSelect => brightnessMultiSelect;
    internal IReadOnlyCollection<string> SelectedBrightnessIds => multiSelection.ToArray();
    internal string? SelectedDisplayId => selectedDisplayId;
    private bool CanRebuild => !closed && !dpiBusy && !dpiDragging && keepOpenCount == 0 &&
        !body.IsMouseCaptureWithin && activeDpiSlider?.IsKeyboardFocusWithin != true &&
        !(Keyboard.FocusedElement is Slider slider && body.IsAncestorOf(slider));

    private DisplayInfo? ResolveSelectedDisplay()
    {
        var displays = owner.OrderedDisplays().ToList();
        var wanted = selectedDisplayId ?? owner.Preferences.QuickSelectedDisplayId ?? owner.Preferences.QuickMoreDisplayId;
        var selected = displays.FirstOrDefault(d => string.Equals(d.Id, wanted, StringComparison.OrdinalIgnoreCase));
        if (selected == null)
        {
            var trayScreen = System.Windows.Forms.Screen.FromHandle(Native.FindWindow("Shell_TrayWnd", null));
            selected = displays.FirstOrDefault(d => d.Device == trayScreen.DeviceName) ?? displays.FirstOrDefault(d => d.Primary) ?? displays.FirstOrDefault();
        }
        if (selectedDisplayId != selected?.Id) { CloseIdentifyWindows(); selectedDisplayId = selected?.Id; }
        // Keep the last choice through a transient empty scan, but fall back immediately
        // if other screens remain. Old per-device disclosure settings are harmless.
        if (selected != null && owner.Preferences.QuickSelectedDisplayId != selected.Id)
        {
            owner.Preferences.QuickSelectedDisplayId = selected.Id;
            if (owner.Preferences.QuickMoreDisplayId != null) owner.Preferences.QuickMoreDisplayId = selected.Id;
            owner.SavePreferences();
        }
        return selected;
    }

    private void SelectDisplay(string id)
    {
        if (!CanRebuild || !owner.Displays.Any(d => d.Id == id)) return;
        CloseIdentifyWindows();
        if (brightnessMultiSelect)
        {
            if (!multiSelection.Remove(id)) multiSelection.Add(id);
            multiBrightnessError = null;
        }
        else
        {
            if (selectedDisplayId == id) return;
            selectedDisplayId = owner.Preferences.QuickSelectedDisplayId = id;
            if (owner.Preferences.QuickMoreDisplayId != null) owner.Preferences.QuickMoreDisplayId = id;
            owner.SavePreferences();
        }
        Render(); controlsScroll.ScrollToTop();
        // Rebuilding the controls must not strand keyboard focus on a removed tile.
        if (picker.Children.OfType<Canvas>().FirstOrDefault() is { } canvas)
            canvas.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == id)?.Focus();
    }

    private void RenderDisplayPicker(DisplayInfo? selected)
    {
        var mode = Ui.Button(L.T(brightnessMultiSelect ? "quick.multiDone" : "quick.multiStart"), () =>
        {
            if (!CanRebuild) return;
            CloseIdentifyWindows(); brightnessMultiSelect = !brightnessMultiSelect; multiSelection.Clear(); multiBrightnessError = null;
            if (brightnessMultiSelect && selectedDisplayId != null) multiSelection.Add(selectedDisplayId);
            Render(); controlsScroll.ScrollToTop();
        });
        mode.FontSize = 11; mode.Padding = new Thickness(8, 4, 8, 4);
        mode.IsEnabled = selected != null || brightnessMultiSelect;
        mode.Background = Ui.Brush(brightnessMultiSelect ? "#E6F2F4" : "#FFFFFF");
        AutomationProperties.SetAutomationId(mode, "quick-multi-toggle");
        var heading = Ui.Row(Ui.Text(AppBrand.Name, 13, "#303846", true), mode);
        heading.Margin = new Thickness(0, 0, 0, 12); picker.Children.Add(heading);
        if (selected == null) return;

        var identify = Ui.Button(L.T("quick.identify"), () => IdentifySelectedDisplay());
        identify.FontSize = 11; identify.Padding = new Thickness(8, 4, 8, 4);
        identify.IsEnabled = !brightnessMultiSelect || multiSelection.Count > 0;
        AutomationProperties.SetAutomationId(identify, "quick-identify");
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (brightnessMultiSelect)
        {
            var all = Ui.Button(L.T(multiSelection.Count == owner.Displays.Count ? "quick.multiNone" : "quick.multiAll"), () =>
            {
                if (!CanRebuild) return;
                CloseIdentifyWindows();
                if (multiSelection.Count == owner.Displays.Count) multiSelection.Clear();
                else multiSelection.UnionWith(owner.Displays.Select(d => d.Id));
                multiBrightnessError = null; Render();
            });
            all.FontSize = 11; all.Padding = new Thickness(5, 4, 5, 4); all.Margin = new Thickness(0, 0, 4, 0);
            AutomationProperties.SetAutomationId(all, "quick-multi-all"); actions.Children.Add(all);
        }
        actions.Children.Add(identify);
        picker.Children.Add(Ui.Row(Ui.Text(brightnessMultiSelect ? L.F("quick.multiCount", multiSelection.Count) : L.T("quick.choose"), 12, "#616161"), actions, 6));
        var canvas = new Canvas { Height = owner.Displays.Count == 1 ? 90 : 116, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetAutomationId(canvas, "quick-display-map");
        AutomationProperties.SetName(canvas, L.T("quick.choose"));
        var displays = owner.Displays.OrderBy(d => d.X).ThenBy(d => d.Y).ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var targets = brightnessMultiSelect ? owner.Displays.Where(d => multiSelection.Contains(d.Id)).ToList() : owner.BrightnessTargets(selected);
        var linked = targets.Count > 1 ? targets.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>();
        void Draw()
        {
            string? focusedId = canvas.IsKeyboardFocusWithin ? (Keyboard.FocusedElement as Button)?.Tag as string : null;
            canvas.Children.Clear(); multiLevelLabels.Clear();
            foreach (var tile in QuickDisplayMap.Project(displays, canvas.ActualWidth > 0 ? canvas.ActualWidth : Math.Max(40, Width - 38), canvas.Height))
            {
                var display = tile.Display; bool active = brightnessMultiSelect ? multiSelection.Contains(display.Id) : display.Id == selected.Id;
                var button = Ui.Button("", () => SelectDisplay(display.Id));
                button.Tag = display.Id; button.ToolTip = display.Name;
                button.Width = tile.Bounds.Width; button.Height = tile.Bounds.Height;
                button.Padding = new Thickness(0);
                button.Background = Ui.Brush(active ? "#E6F2F4" : "#FFFFFF");
                button.BorderBrush = Ui.Brush(active ? "#007A8A" : "#D1D1D1");
                button.BorderThickness = new Thickness(active ? 2 : 1);
                if (brightnessMultiSelect)
                {
                    var face = new Grid();
                    var level = Ui.Text(owner.GetCombinedBrightness(display) + "%", 11, active ? "#303846" : "#7A8699");
                    level.HorizontalAlignment = HorizontalAlignment.Center; level.VerticalAlignment = VerticalAlignment.Bottom;
                    level.Margin = new Thickness(0, 0, 0, 4); face.Children.Add(level); multiLevelLabels[display.Id] = level;
                    if (active && tile.Bounds.Height >= 35)
                    {
                        var check = Ui.Icon("\uE73E", 11); check.HorizontalAlignment = HorizontalAlignment.Right;
                        check.VerticalAlignment = VerticalAlignment.Top; check.Margin = new Thickness(0, 4, 5, 0); face.Children.Add(check);
                    }
                    button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    // The shared template centers content vertically, so size its face explicitly.
                    face.Height = Math.Max(12, tile.Bounds.Height - 5); button.Content = face;
                }
                else if (active) button.Content = Ui.Icon("\uE73E", 16);
                else if (linked.Contains(display.Id)) button.Content = Ui.Icon("\uE71B", 13);
                AutomationProperties.SetAutomationId(button, "quick-display-select-" + display.Number);
                AutomationProperties.SetName(button, display.Name);
                AutomationProperties.SetItemStatus(button, active ? L.T("quick.selected") : linked.Contains(display.Id) ? L.T("quick.linked") : "");
                button.KeyDown += (_, e) =>
                {
                    if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
                    var index = Array.IndexOf(displays, display);
                    int delta = e.Key is Key.Left or Key.Up ? -1 : 1;
                    string nextId = displays[(index + delta + displays.Length) % displays.Length].Id;
                    if (brightnessMultiSelect) canvas.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == nextId)?.Focus();
                    else SelectDisplay(nextId);
                    e.Handled = true;
                };
                Canvas.SetLeft(button, tile.Bounds.Left); Canvas.SetTop(button, tile.Bounds.Top); canvas.Children.Add(button);
            }
            if (focusedId != null) canvas.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == focusedId)?.Focus();
        }
        canvas.SizeChanged += (_, _) => Draw(); Draw(); picker.Children.Add(canvas);
        if (!brightnessMultiSelect && targets.Count > 1)
        {
            var note = Ui.Text(L.F("quick.syncCount", targets.Count), 11, "#617681");
            note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 0, 0, 8);
            AutomationProperties.SetAutomationId(note, "quick-brightness-scope"); picker.Children.Add(note);
        }
        picker.Children.Add(CompactLine());
    }

    private void IdentifySelectedDisplay()
    {
        CloseIdentifyWindows();
        foreach (var display in owner.Displays.Where(d => brightnessMultiSelect ? multiSelection.Contains(d.Id) : d.Id == selectedDisplayId))
        {
            var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == display.Device);
            if (screen == null) continue;
            var overlay = new DisplayIdentifyWindow(screen.Bounds); identifyWindows.Add(overlay);
            overlay.Closed += (_, _) => identifyWindows.Remove(overlay); overlay.Show();
        }
    }

    private void CloseIdentifyWindows()
    {
        foreach (var window in identifyWindows.ToArray()) window.Close();
    }
}

internal sealed record QuickDisplayTile(DisplayInfo Display, Rect Bounds);

internal static class QuickDisplayMap
{
    internal static IReadOnlyList<QuickDisplayTile> Project(IReadOnlyList<DisplayInfo> displays, double width, double height)
    {
        if (displays.Count == 0 || width <= 0 || height <= 0) return Array.Empty<QuickDisplayTile>();
        // Cloned outputs share an origin. Subdivide their common tile so every
        // physical output remains selectable instead of covering another button.
        var groups = displays.GroupBy(d => (d.X, d.Y)).ToArray();
        double left = displays.Min(d => (double)d.X), top = displays.Min(d => (double)d.Y);
        double right = displays.Max(d => (double)d.X + Math.Max(1, d.Current.Width));
        double bottom = displays.Max(d => (double)d.Y + Math.Max(1, d.Current.Height));
        double scale = Math.Min(width / (right - left), height / (bottom - top));
        double offsetX = (width - (right - left) * scale) / 2, offsetY = (height - (bottom - top) * scale) / 2;
        var result = new List<QuickDisplayTile>();
        foreach (var group in groups)
        {
            double groupWidth = Math.Max(1, group.Max(d => d.Current.Width)) * scale;
            double groupHeight = Math.Max(1, group.Max(d => d.Current.Height)) * scale;
            var members = group.ToArray();
            for (int i = 0; i < members.Length; i++)
            {
                double slotWidth = groupWidth / members.Length, gap = Math.Min(3, Math.Min(slotWidth, groupHeight) / 6);
                double tileWidth = Math.Max(1, slotWidth - 2 * gap), tileHeight = Math.Max(1, groupHeight - 2 * gap);
                if (members.Length > 1)
                {
                    double fit = Math.Min(tileWidth / Math.Max(1, members[i].Current.Width), tileHeight / Math.Max(1, members[i].Current.Height));
                    tileWidth = Math.Max(1, members[i].Current.Width * fit); tileHeight = Math.Max(1, members[i].Current.Height * fit);
                }
                var bounds = new Rect(offsetX + (group.Key.X - left) * scale + i * slotWidth + gap,
                    offsetY + (group.Key.Y - top) * scale + (groupHeight - tileHeight) / 2, tileWidth, tileHeight);
                result.Add(new(members[i], bounds));
            }
        }
        return result;
    }
}

internal sealed class DisplayIdentifyWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    internal DisplayIdentifyWindow(System.Drawing.Rectangle bounds)
    {
        Ui.ConfigureWindow(this);
        Title = L.T("quick.identify"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; IsHitTestVisible = false; Width = Height = 1;
        var check = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(16), Background = Ui.Brush("#E6F2F4"),
            BorderBrush = Ui.Brush("#007A8A"), BorderThickness = new Thickness(2), Child = Ui.Icon("\uE73E", 32),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Content = new Border { BorderBrush = Ui.Brush("#007A8A"), BorderThickness = new Thickness(4), Child = check };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd, -20, Native.GetWindowLong(hwnd, -20) | 0x20 | 0x8000000 | 0x80);
        };
        Loaded += (_, _) =>
        {
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10);
            timer.Start();
        };
        timer.Tick += (_, _) => Close();
        Closed += (_, _) => timer.Stop();
    }
}
