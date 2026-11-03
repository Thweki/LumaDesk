// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LumaDesk;

// Keep one monitor anchor for the lifetime of the popup. WPF's global WorkArea
// uses system-DPI logical coordinates; it cannot be used as per-monitor Left/Top.
internal sealed class PopupPlacement
{
    private readonly Window window;
    private readonly Func<double> desiredHeight;
    private HwndSource? source;
    private string? monitorDevice;
    private bool queued, closed, positioning;

    internal PopupPlacement(Window window, Func<double> desiredHeight)
    {
        this.window = window; this.desiredHeight = desiredHeight;
        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(Hook);
        };
        window.Loaded += (_, _) => Position();
        window.DpiChanged += (_, _) => Schedule();
        window.SizeChanged += (_, _) => Schedule();
        window.LocationChanged += (_, _) => Schedule();
        window.Closed += (_, _) => { closed = true; source?.RemoveHook(Hook); source = null; };
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x007E or 0x001A) Schedule(); // DPI, display, work-area changes
        // WPF must still process DPI changes to redraw text and resize its HWND.
        return IntPtr.Zero;
    }

    private void Schedule()
    {
        if (queued || closed || !window.IsVisible) return;
        queued = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            queued = false;
            Position();
        }));
    }

    internal void Position()
    {
        if (closed || positioning || source == null || !window.IsVisible) return;
        positioning = true;
        try
        {
            var monitor = ReadAnchor();
            if (monitor == null) return;
            var dpi = VisualTreeHelper.GetDpi(window);
            var target = Calculate(monitor.Value.Work, 360, desiredHeight(), dpi.DpiScaleX, dpi.DpiScaleY);
            // Retain the desired content height separately, so a temporarily small
            // work area does not permanently shrink the panel after scaling back.
            window.MinHeight = 0;
            window.Width = (target.Right - target.Left) / dpi.DpiScaleX;
            window.Height = (target.Bottom - target.Top) / dpi.DpiScaleY;
            if (!Native.GetWindowRect(source.Handle, out var current) ||
                current.Left != target.Left || current.Top != target.Top || current.Right != target.Right || current.Bottom != target.Bottom)
                Native.SetWindowPos(source.Handle, IntPtr.Zero, target.Left, target.Top,
                    target.Right - target.Left, target.Bottom - target.Top, 0x0014); // no activation or Z-order change
        }
        finally { positioning = false; }
    }

    private Native.MonitorInfo? ReadAnchor()
    {
        Native.MonitorInfo? found = null;
        if (monitorDevice != null)
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr dc, ref Native.Rect rect, IntPtr data) =>
            {
                var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
                if (Native.GetMonitorInfo(handle, ref info) && info.Device == monitorDevice) { found = info; return false; }
                return true;
            }, IntPtr.Zero);
        if (found != null) return found;
        // Use the notification-area taskbar on first open (or if its monitor was removed).
        var monitor = Native.MonitorFromWindow(Native.FindWindow("Shell_TrayWnd", null), 1);
        var fallback = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref fallback)) return null;
        monitorDevice = fallback.Device;
        return fallback;
    }

    internal static Native.Rect Calculate(Native.Rect work, double width, double height, double scaleX, double scaleY)
    {
        int availableWidth = Math.Max(1, work.Right - work.Left), availableHeight = Math.Max(1, work.Bottom - work.Top);
        int gapX = Math.Min((int)Math.Round(12 * scaleX), (availableWidth - 1) / 2);
        int gapY = Math.Min((int)Math.Round(12 * scaleY), (availableHeight - 1) / 2);
        int pixelWidth = Math.Clamp((int)Math.Round(width * scaleX), 1, availableWidth - 2 * gapX);
        int pixelHeight = Math.Clamp((int)Math.Round(height * scaleY), 1, availableHeight - 2 * gapY);
        return new Native.Rect { Left = work.Right - gapX - pixelWidth, Top = work.Bottom - gapY - pixelHeight, Right = work.Right - gapX, Bottom = work.Bottom - gapY };
    }
}
