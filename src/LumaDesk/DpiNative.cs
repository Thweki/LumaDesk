// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Runtime.InteropServices;

namespace LumaDesk;

internal static class DpiNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Header { public int Type; public uint Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] internal struct Source { public Luid Adapter; public uint Id, ModeIndex, Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] internal struct Target
    {
        public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling;
        public Rational Refresh; public uint ScanLine; public int Available; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Path { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct Mode { [FieldOffset(0)] public uint Type; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceName
    {
        public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    // These DPI packet types (-3/-4) are undocumented Windows compatibility APIs.
    // Validate every relative index; never infer or clamp unsupported/custom scaling.
    [StructLayout(LayoutKind.Sequential)] internal struct GetScale { public Header Header; public int Minimum, Current, Maximum; }
    [StructLayout(LayoutKind.Sequential)] internal struct SetScale { public Header Header; public int Relative; }
    [StructLayout(LayoutKind.Explicit, Size = 80)] internal struct PreferredMode
    {
        [FieldOffset(0)] public Header Header;
        [FieldOffset(20)] public uint Width;
        [FieldOffset(24)] public uint Height;
    }
    [DllImport("user32.dll")] internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] internal static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] Path[] paths, ref uint modeCount, [Out] Mode[] modes, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetSourceName(ref SourceName packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetDpi(ref GetScale packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetPreferred(ref PreferredMode packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] internal static extern int SetDpi(ref SetScale packet);
    public static Header Packet<T>(int type, Luid adapter, uint id) => new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = adapter, Id = id };
}
