// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LumaDesk;

internal sealed class BrightnessOsd : Window
{
    private readonly TextBlock label = Ui.Text("", 13, "#FFFFFF");
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    internal BrightnessOsd()
    {
        Ui.ConfigureWindow(this); Width = 240; Height = 76; WindowStyle = WindowStyle.None;
        AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent; ShowActivated = false;
        ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        var stack = new StackPanel(); stack.Children.Add(label); stack.Children.Add(progress);
        Content = new Border { Background = Ui.Brush("#EE252525"), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = stack };
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; Native.SetWindowLong(h, -20, Native.GetWindowLong(h, -20) | 0x20 | 0x8000000 | 0x80); };
        timer.Tick += (_, _) => { timer.Stop(); Hide(); };
        Closed += (_, _) => timer.Stop();
    }
    internal void ShowLevel(DisplayInfo display, int value)
    {
        label.Text = display.Name + "  " + value + "%"; label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextWrapping = TextWrapping.NoWrap;
        progress.Value = value; Show();
        var screen = System.Windows.Forms.Screen.AllScreens;
        var work = Array.Find(screen, s => s.DeviceName == display.Device)?.WorkingArea ?? System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        int width = (int)(Width * dpi.DpiScaleX), height = (int)(Height * dpi.DpiScaleY);
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), work.Left + (work.Width - width) / 2, work.Bottom - height - 36, width, height, 0x10);
        timer.Stop(); timer.Start();
    }
}
