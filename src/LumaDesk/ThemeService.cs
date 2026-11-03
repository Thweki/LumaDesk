// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Interop;

namespace LumaDesk;

internal static class ThemeService
{
    private static readonly Dictionary<(string Hex, bool Text), SolidColorBrush> brushes = new();
    private static readonly Dictionary<(string Hex, bool Text), ThemeColor> colors = new();
    private sealed class ThemeColor : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(Color), typeof(ThemeColor));
        public Color Value { get => (Color)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }
    internal static bool Dark { get; private set; }
    internal static SolidColorBrush Brush(string hex, bool text = false)
    {
        if (!brushes.TryGetValue((hex, text), out var brush))
        {
            var color = new ThemeColor { Value = Map((Color)ColorConverter.ConvertFromString(hex), text) };
            colors[(hex, text)] = color;
            brushes[(hex, text)] = brush = new SolidColorBrush();
            // A binding keeps WPF templates from freezing shared theme brushes.
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ThemeColor.Value)) { Source = color, Mode = BindingMode.OneWay });
        }
        return brush;
    }
    private static Color Map(Color color, bool text)
    {
        if (SystemParameters.HighContrast) { var system = text ? SystemColors.WindowTextColor : SystemColors.WindowColor; system.A = color.A; return system; }
        if (!Dark) return color;
        double high = Math.Max(color.R, Math.Max(color.G, color.B)), low = Math.Min(color.R, Math.Min(color.G, color.B));
        if (text)
        {
            if (high - low > 55) return Color.FromArgb(color.A, (byte)Math.Min(255, color.R + 60), (byte)Math.Min(255, color.G + 60), (byte)Math.Min(255, color.B + 60));
            byte shade = high < 100 ? (byte)235 : (byte)190; return Color.FromArgb(color.A, shade, shade, shade);
        }
        if (high > 175)
        {
            if (high - low > 45) return Color.FromArgb(color.A, (byte)(color.R * .23), (byte)(color.G * .23), (byte)(color.B * .23));
            byte shade = high >= 250 ? (byte)45 : high >= 235 ? (byte)32 : (byte)62;
            return Color.FromArgb(color.A, shade, shade, shade);
        }
        return color;
    }
    internal static void Apply(string preference)
    {
        bool dark = preference == "dark";
        if (preference == "auto")
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
            catch { }
        }
        Dark = dark;
        foreach (var item in colors) item.Value.Value = Map((Color)ColorConverter.ConvertFromString(item.Key.Hex), item.Key.Text);
        var resources = System.Windows.Application.Current.Resources;
        foreach (string hex in new[] { "242424", "616161", "757575", "007A8A", "004E59", "8A8A8A", "B8B8B8", "D1D1D1", "D5D5D5", "D6D6D6", "D9D9D9", "E1E1E1", "E4F2F3", "E5E5E5", "F3F3F3", "FBFBFB", "FFFFFF", "C42B1C", "A82418" })
        {
            resources["Theme" + hex] = Brush("#" + hex); resources["Text" + hex] = Brush("#" + hex, true);
        }
        resources["Ink"] = Brush("#242424", true); resources["Muted"] = Brush("#616161", true);
        resources[SystemColors.ControlBrushKey] = Brush("#F3F3F3");
        resources[SystemColors.WindowBrushKey] = Brush("#FFFFFF");
        resources[SystemColors.ControlTextBrushKey] = Brush("#242424", true);
        resources[SystemColors.WindowTextBrushKey] = Brush("#242424", true);
        resources[SystemColors.HighlightBrushKey] = Brush("#007A8A");
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) continue;
            int darkMode = Dark ? 1 : 0; Native.DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        }
    }
}
