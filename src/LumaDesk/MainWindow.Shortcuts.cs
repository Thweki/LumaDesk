// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace LumaDesk;

public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private readonly Dictionary<int, Action> shortcutActions = new();
    private readonly Dictionary<string, LatestValueWriter> shortcutWriters = new();
    private readonly Dictionary<string, int> shortcutValues = new();
    private BrightnessOsd? brightnessOsd;
    private bool recordingShortcut;

    private void UnregisterShortcuts()
    {
        foreach (int id in shortcutActions.Keys) Native.UnregisterHotKey(hwnd, id);
        shortcutActions.Clear();
        foreach (var writer in shortcutWriters.Values) writer.Stop();
        shortcutWriters.Clear(); shortcutValues.Clear();
    }
    private void ConfigureHotkeys()
    {
        if (hwnd == IntPtr.Zero) return;
        UnregisterShortcuts();
        if (!Preferences.Hotkeys || recordingShortcut) return;
        var used = new HashSet<(uint, uint)>(); var failed = new List<string>(); int id = 0;
        void Register(HotkeyBinding? binding, string name, Action action)
        {
            if (binding == null || binding.Key == 0) return;
            int token = ++id;
            if ((binding.Modifiers & 11) == 0 || !used.Add((binding.Modifiers, binding.Key)) || !Native.RegisterHotKey(hwnd, token, binding.Modifiers | 0x4000, binding.Key))
            { failed.Add(name); return; }
            shortcutActions[token] = action;
        }
        Register(Preferences.BrightnessUp, L.T("s768e8a9c2a"), () => AdjustShortcut(5));
        Register(Preferences.BrightnessDown, L.T("s970eb36fb3"), () => AdjustShortcut(-5));
        Register(Preferences.ResetDimmingKey, L.T("s8e6dbec08e"), ResetDimming);
        foreach (var scene in Preferences.Presets) Register(scene.Shortcut, scene.Name, () => _ = RunAsync(async () => await ApplySceneAsync(scene)));
        if (failed.Count > 0) Status(L.T("shortcut.conflict") + string.Join("、", failed), true);
    }
    private void HandleShortcut(int id) { if (shortcutActions.TryGetValue(id, out var action)) action(); }
    private DisplayInfo? ShortcutDisplay()
    {
        string device = Preferences.HotkeyTarget == "window" ? Forms.Screen.FromHandle(GetForegroundWindow()).DeviceName : Forms.Screen.FromPoint(Forms.Cursor.Position).DeviceName;
        if (Preferences.HotkeyTarget != "pointer" && Preferences.HotkeyTarget != "window") return Displays.FirstOrDefault(d => d.Id == Preferences.HotkeyTarget) ?? Displays.FirstOrDefault(d => d.Primary);
        return Displays.FirstOrDefault(d => d.Device == device) ?? Selected;
    }
    private void AdjustShortcut(int delta)
    {
        var display = ShortcutDisplay(); if (display == null) return;
        string id = display.Id;
        if (!shortcutWriters.TryGetValue(id, out var writer))
        {
            writer = new LatestValueWriter(async value =>
            {
                if (sceneBusy || modePending) return;
                var current = Displays.FirstOrDefault(d => d.Id == id);
                if (current == null) return;
                await ChangeCombinedBrightness(current, value);
                brightnessOsd ??= new BrightnessOsd(); brightnessOsd.ShowLevel(current, GetCombinedBrightness(current));
            }, (_, error) => { if (error != null) Status(error.Message, true); }, idle: () => { shortcutValues.Remove(id); if (IsVisible) Render(); quick?.Render(); });
            shortcutWriters[id] = writer;
        }
        int next = Math.Clamp(shortcutValues.GetValueOrDefault(id, GetCombinedBrightness(display)) + delta, 0, 100);
        shortcutValues[id] = next; writer.Request(next);
    }
    private static string ShortcutText(HotkeyBinding? binding)
    {
        if (binding == null || binding.Key == 0) return L.T("shortcut.unassigned");
        var parts = new List<string>();
        if ((binding.Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((binding.Modifiers & 1) != 0) parts.Add("Alt");
        if ((binding.Modifiers & 4) != 0) parts.Add("Shift");
        if ((binding.Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)binding.Key).ToString()); return string.Join(" + ", parts);
    }
    private UIElement ShortcutEditor(HotkeyBinding? current, Action<HotkeyBinding> changed)
    {
        var box = new TextBox { Text = ShortcutText(current), IsReadOnly = true, MinWidth = 175, ToolTip = L.T("shortcut.record") };
        box.GotKeyboardFocus += (_, _) => { recordingShortcut = true; UnregisterShortcuts(); };
        box.LostKeyboardFocus += (_, _) => { recordingShortcut = false; ConfigureHotkeys(); };
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab) return;
            e.Handled = true;
            if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var modifiers = Keyboard.Modifiers;
            if ((key is Key.Back or Key.Delete) && modifiers == ModifierKeys.None)
            { var empty = new HotkeyBinding { Key = 0 }; changed(empty); box.Text = ShortcutText(empty); return; }
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) { Status(L.T("shortcut.modifier"), true); return; }
            uint mask = (modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0) | (modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0) | (modifiers.HasFlag(ModifierKeys.Windows) ? 8u : 0);
            var binding = new HotkeyBinding { Modifiers = mask, Key = (uint)KeyInterop.VirtualKeyFromKey(key) }; changed(binding); box.Text = ShortcutText(binding);
        };
        return box;
    }
    private void RenderShortcuts()
    {
        Heading(L.T("s864200bbb2"), L.T("shortcut.description"));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Text(L.T("sc6a591a792")), Toggle(Preferences.Hotkeys, v => { Preferences.Hotkeys = v; Save(); ConfigureHotkeys(); }))));
        var shortcuts = new StackPanel();
        foreach (var item in new (string Label, HotkeyBinding Binding, Action<HotkeyBinding> Save)[] {
            (L.T("s768e8a9c2a"), Preferences.BrightnessUp, b => Preferences.BrightnessUp = b),
            (L.T("s970eb36fb3"), Preferences.BrightnessDown, b => Preferences.BrightnessDown = b),
            (L.T("s8e6dbec08e"), Preferences.ResetDimmingKey, b => Preferences.ResetDimmingKey = b) })
        {
            if (shortcuts.Children.Count > 0) shortcuts.Children.Add(Ui.Line());
            shortcuts.Children.Add(Ui.Row(Ui.Text(item.Label), ShortcutEditor(item.Binding, b => { item.Save(b); Save(); ConfigureHotkeys(); })));
        }
        PageContent.Children.Add(Ui.Card(shortcuts));
        var ids = new[] { "pointer", "window" }.Concat(Displays.Select(d => d.Id)).ToArray();
        var target = new ComboBox { ItemsSource = new[] { L.T("shortcut.pointer"), L.T("shortcut.window") }.Concat(Displays.Select(d => d.Name)).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(ids, Preferences.HotkeyTarget)) };
        target.SelectionChanged += (_, _) => { if (target.SelectedIndex >= 0) { Preferences.HotkeyTarget = ids[target.SelectedIndex]; Save(); } };
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Text(L.T("shortcut.target")), target)));
    }
}
