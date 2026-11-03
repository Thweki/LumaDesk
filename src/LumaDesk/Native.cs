// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Runtime.InteropServices;

namespace LumaDesk;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        public static DisplayDevice Create() => new() { Size = Marshal.SizeOf<DisplayDevice>() };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int X, Y;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, Width, Height, DisplayFlags, Frequency, ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        public static DevMode Create() => new() { Size = (ushort)Marshal.SizeOf<DevMode>(), DeviceName = "", FormName = "" };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PhysicalMonitor { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    internal delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice result, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplaySettings(string device, int index, ref DevMode mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int ChangeDisplaySettingsEx(string device, ref DevMode mode, IntPtr hwnd, uint flags, IntPtr param);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")] internal static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr handle);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
