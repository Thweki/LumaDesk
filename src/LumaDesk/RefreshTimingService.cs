// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace LumaDesk;

internal sealed record RefreshTiming(double? DesktopHz = null, double? SignalHz = null,
    bool? DynamicRefreshEnabled = null, string Error = "");

internal interface IRefreshTimingBackend
{
    void ValidateIdentity(DisplayInfo display);
    (DpiNative.Path[] Paths, RefreshTimingNative.Mode[] Modes) Query(uint flags);
    string SourceName(DpiNative.Source source);
}

internal static class RefreshTimingNative
{
    // DISPLAYCONFIG_MODE_INFO: targetVideoSignalInfo.vSyncFreq starts at byte 32.
    // Keep the full native union size even though this reader only needs vSyncFreq.
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct Mode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public DpiNative.Luid Adapter;
        [FieldOffset(32)] public DpiNative.Rational VerticalSync;
    }
    [DllImport("user32.dll", EntryPoint = "QueryDisplayConfig")]
    internal static extern int Query(uint flags, ref uint pathCount, [Out] DpiNative.Path[] paths,
        ref uint modeCount, [Out] Mode[] modes, IntPtr topology);
}

internal static class RefreshTimingService
{
    internal const string Flag = "LUMADESK_EXPERIMENTAL_REFRESH_TIMING";
    internal static bool IsEnabled => FlagEnabled(Environment.GetEnvironmentVariable(Flag));
    internal static bool FlagEnabled(string? value) => value == "1";
    private const uint ActiveVirtualModes = 0x12;
    private const uint VirtualRefreshAware = 0x40;

    internal static RefreshTiming Read(DisplayInfo display) => Read(display, IsEnabled, new NativeBackend());

    internal static RefreshTiming Read(DisplayInfo display, bool enabled, IRefreshTimingBackend backend)
    {
        // Gate the reader as well as the UI: disabled means no native queries.
        if (!enabled) return new(Error: L.T("timing.disabled"));
        try
        {
            backend.ValidateIdentity(display);
            bool aware = true;
            (DpiNative.Path[] Paths, RefreshTimingNative.Mode[] Modes) snapshot;
            try { snapshot = backend.Query(ActiveVirtualModes | VirtualRefreshAware); }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 87 or 50)
            {
                // Windows 10 / older drivers cannot report DRR. Never call that "off".
                aware = false;
                snapshot = backend.Query(ActiveVirtualModes);
            }
            var matches = snapshot.Paths.Where(p => (p.Flags & 1) != 0 &&
                string.Equals(backend.SourceName(p.Source), display.Device, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) return new(Error: L.T("timing.ambiguous"));
            var result = Decode(matches[0], snapshot.Modes, aware);
            // A hotplug can recycle a GDI name while the query is in progress.
            backend.ValidateIdentity(display);
            return result;
        }
        catch (Exception ex) { return new(Error: L.F("timing.failed", ex.Message)); }
    }

    internal static RefreshTiming Decode(DpiNative.Path path, RefreshTimingNative.Mode[] modes, bool refreshAware)
    {
        if ((path.Flags & 1) == 0 || path.Target.Available == 0)
            return new(Error: L.T("timing.ambiguous"));
        // With QDC_VIRTUAL_MODE_AWARE, paths supporting virtual modes pack the
        // target index into the HIGH word; the low word is the desktop-image index.
        bool virtualMode = (path.Flags & 8) != 0;
        uint index = virtualMode ? path.Target.ModeIndex >> 16 : path.Target.ModeIndex;
        uint invalid = virtualMode ? 0xffffu : uint.MaxValue;
        double? signal = null;
        if (index != invalid && index < modes.Length)
        {
            var mode = modes[index];
            if (mode.Type == 2 && mode.Id == path.Target.Id && mode.Adapter.Equals(path.Target.Adapter))
                signal = Hertz(mode.VerticalSync);
        }
        return new(Hertz(path.Target.Refresh), signal, refreshAware ? (path.Flags & 0x10) != 0 : null);
    }

    private static double? Hertz(DpiNative.Rational value) =>
        value.Numerator == 0 || value.Denominator == 0 ? null : (double)value.Numerator / value.Denominator;

    private sealed class NativeBackend : IRefreshTimingBackend
    {
        public void ValidateIdentity(DisplayInfo display) => DisplayService.ValidateIdentity(display.Device, display.Id);
        public string SourceName(DpiNative.Source source)
        {
            var name = new DpiNative.SourceName { Header = DpiNative.Packet<DpiNative.SourceName>(1, source.Adapter, source.Id) };
            int status = DpiNative.GetSourceName(ref name);
            if (status != 0) throw new Win32Exception(status);
            return name.Name;
        }
        public (DpiNative.Path[] Paths, RefreshTimingNative.Mode[] Modes) Query(uint flags)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int status = DpiNative.GetDisplayConfigBufferSizes(flags, out uint pc, out uint mc);
                if (status != 0) throw new Win32Exception(status);
                if (pc > 1024 || mc > 4096) throw new InvalidOperationException(L.T("s8833926459"));
                var paths = new DpiNative.Path[pc]; var modes = new RefreshTimingNative.Mode[mc];
                status = RefreshTimingNative.Query(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
                if (status == 122) continue;
                if (status != 0) throw new Win32Exception(status);
                if (pc > paths.Length || mc > modes.Length) throw new InvalidOperationException(L.T("s8833926459"));
                Array.Resize(ref paths, (int)pc); Array.Resize(ref modes, (int)mc);
                return (paths, modes);
            }
            throw new InvalidOperationException(L.T("s3bffd3b4df"));
        }
    }
}
