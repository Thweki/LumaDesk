// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LumaDesk;

internal static class Ui
{
    internal static void ConfigureWindow(Window window)
    {
        // Implicit Window styles do not match derived WPF window types.
        window.Style = (Style)System.Windows.Application.Current.FindResource(typeof(Window));
    }
    public static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    public static TextBlock Text(string text, double size = 13, string color = "#242424", bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    public static TextBlock Icon(string glyph, double size = 19, string color = "#007A8A") => new() { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        if (primary) button.Style = (Style)System.Windows.Application.Current.FindResource("Primary");
        button.Click += (_, _) => action(); return button;
    }
    public static Border Card(UIElement child, Thickness? padding = null) => new() { Child = child, Padding = padding ?? new Thickness(18), Background = Brush("#FFFFFF"), CornerRadius = new CornerRadius(8), BorderBrush = Brush("#E5E5E5"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
    public static Border Line() => new() { Height = 1, Background = Brush("#EBEBEB"), Margin = new Thickness(0, 10, 0, 10) };
    public static Grid Row(UIElement left, UIElement right, double gap = 14)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, gap, 0)); grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    public static StackPanel Labels(string title, string? detail = null)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(Text(title));
        if (!string.IsNullOrEmpty(detail)) { var help = Text(detail, 11, "#858B97"); help.Margin = new Thickness(0, 5, 0, 0); stack.Children.Add(help); }
        return stack;
    }
    public static Border Badge(string label, string color = "#007A8A", string background = "#E4F2F3") => new() { Child = Text(label, 10, color, true), Background = Brush(background), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center };
    public static Grid IconLabel(string icon, string text)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.Children.Add(Icon(icon));
        var label = Text(text); Grid.SetColumn(label, 1); grid.Children.Add(label); return grid;
    }
}
