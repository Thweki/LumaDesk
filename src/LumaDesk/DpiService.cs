// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using Microsoft.Win32;
using System;
using System.Linq;
using System.Threading;

namespace LumaDesk;

internal static class DpiService
{
    private static DpiNative.Path[] Paths()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int status = DpiNative.GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount);
            if (status != 0) throw new InvalidOperationException(L.F("s2a25d4c4bd", status));
            if (pathCount > 128 || modeCount > 1024) throw new InvalidOperationException(L.T("s8833926459"));
            var paths = new DpiNative.Path[pathCount]; var modes = new DpiNative.Mode[modeCount];
            status = DpiNative.QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status == 122) continue; // Topology changed between buffer sizing and query.
            if (status != 0) throw new InvalidOperationException(L.F("s2046b1ff9b", status));
            return paths.Take((int)pathCount).ToArray();
        }
        throw new InvalidOperationException(L.T("sdd798c7428"));
    }
    private static DpiNative.Path FindPath(string device)
    {
        var paths = Paths();
        foreach (var path in paths)
        {
            var name = new DpiNative.SourceName { Header = DpiNative.Packet<DpiNative.SourceName>(1, path.Source.Adapter, path.Source.Id) };
            if (DpiNative.GetSourceName(ref name) != 0 || !name.Name.Equals(device, StringComparison.OrdinalIgnoreCase)) continue;
            if (paths.Count(p => p.Source.Adapter.Equals(path.Source.Adapter) && p.Source.Id == path.Source.Id) != 1)
                throw new InvalidOperationException(L.T("sd10b18f921"));
            return path;
        }
        throw new InvalidOperationException(L.T("sf3dee96331"));
    }
    private static DpiScaleInfo ReadPath(DpiNative.Path path)
    {
        using var desktop = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop");
        if (Convert.ToInt32(desktop?.GetValue("Win8DpiScaling", 0)) != 0)
            throw new InvalidOperationException(L.T("s8216625441"));
        var packet = new DpiNative.GetScale { Header = DpiNative.Packet<DpiNative.GetScale>(-3, path.Source.Adapter, path.Source.Id) };
        int status = DpiNative.GetDpi(ref packet);
        if (status != 0) throw new InvalidOperationException(L.F("s579608806e", status));
        return DpiRules.FromRelative(packet.Minimum, packet.Current, packet.Maximum);
    }
    public static void Inspect(DisplayInfo display)
    {
        try
        {
            var path = FindPath(display.Device);
            var preferred = new DpiNative.PreferredMode { Header = DpiNative.Packet<DpiNative.PreferredMode>(3, path.Target.Adapter, path.Target.Id) };
            if (DpiNative.GetPreferred(ref preferred) == 0 && preferred.Width > 0 && preferred.Height > 0)
            {
                display.PreferredWidth = display.Orientation % 2 == 0 ? preferred.Width : preferred.Height;
                display.PreferredHeight = display.Orientation % 2 == 0 ? preferred.Height : preferred.Width;
            }
            display.DpiScale = ReadPath(path);
        }
        catch (Exception ex) { display.DpiScale = new DpiScaleInfo { UnavailableReason = ex.Message }; Storage.Log("DPI query: " + ex.Message); }
    }
    public static DpiScaleInfo Read(DisplayInfo display)
    {
        DisplayService.ValidateIdentity(display.Device, display.Id);
        return ReadPath(FindPath(display.Device));
    }
    public static void Set(DisplayInfo display, int percent)
    {
        DisplayService.ValidateIdentity(display.Device, display.Id);
        var path = FindPath(display.Device); var current = ReadPath(path);
        int relative = DpiRules.ToRelative(current, percent); // Exact supported steps only.
        if (current.CurrentPercent == percent) return;
        var packet = new DpiNative.SetScale { Header = DpiNative.Packet<DpiNative.SetScale>(-4, path.Source.Adapter, path.Source.Id), Relative = relative };
        int status = DpiNative.SetDpi(ref packet);
        if (status != 0) throw new InvalidOperationException(L.F("sc6860ec8af", status));
        // Readback must map the device again: DPI changes may also recreate display paths.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (Read(display).CurrentPercent == percent) return;
            Thread.Sleep(50);
        }
        throw new InvalidOperationException(L.T("s8bf1565113"));
    }
}
