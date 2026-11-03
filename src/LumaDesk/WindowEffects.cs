// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace LumaDesk;

internal static class WindowEffects
{
    // A normal composited HWND is required: AllowsTransparency would create a
    // layered window and prevent DWM's real backdrop and rounded-corner policy.
    internal static void ConfigurePopup(Window window, Border surface, Action<bool> applied)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false
        });
        HwndSource? source = null;
        void Apply()
        {
            if (source == null) return;
            int rounded = 2, light = 0;
            Native.DwmSetWindowAttribute(source.Handle, 33, ref rounded, sizeof(int));
            Native.DwmSetWindowAttribute(source.Handle, 20, ref light, sizeof(int));
            bool enabled = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && !SystemParameters.HighContrast && TransparencyEnabled();
            int backdrop = enabled ? 3 : 1;
            enabled &= Native.DwmSetWindowAttribute(source.Handle, 38, ref backdrop, sizeof(int)) >= 0;
            var margins = new Margins(enabled ? -1 : 0);
            enabled &= DwmExtendFrameIntoClientArea(source.Handle, ref margins) >= 0;
            if (!enabled) { backdrop = 1; Native.DwmSetWindowAttribute(source.Handle, 38, ref backdrop, sizeof(int)); }
            window.Background = Brushes.Transparent;
            if (source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            surface.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Ui.Brush(enabled ? "#78F3F3F3" : "#F3F3F3");
            applied(enabled);
        }
        IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message is 0x001A or 0x031A or 0x031E) window.Dispatcher.BeginInvoke((Action)Apply);
            return IntPtr.Zero;
        }
        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(Hook); Apply();
        };
        window.Closed += (_, _) => { source?.RemoveHook(Hook); source = null; };
    }
    private static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
        }
        catch { return false; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
        public Margins(int value) { Left = Right = Top = Bottom = value; }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
