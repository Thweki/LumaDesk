// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LumaDesk;

internal sealed class DisplayService
{
    // All DDC operations are serialized. Physical handles live only within one operation.
    private readonly object gate = new();
    public Task<List<DisplayInfo>> ScanAsync() => Task.Run(() => { lock (gate) return Scan(); });
    private List<DisplayInfo> Scan()
    {
        var result = new List<DisplayInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Native.Rect rect, IntPtr data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) return true;
            var mode = Native.DevMode.Create();
            if (!Native.EnumDisplaySettings(info.Device, -1, ref mode)) return true;
            var device = Native.DisplayDevice.Create();
            Native.EnumDisplayDevices(info.Device, 0, ref device, 1);
            var display = new DisplayInfo
            {
                Id = string.IsNullOrWhiteSpace(device.Id) ? info.Device : DisplayRules.MonitorInstanceId(device.Id),
                Device = info.Device, Name = device.Description ?? L.T("s1a13ea5910"), RegistryPath = device.Key ?? "",
                Number = result.Count + 1, Primary = (info.Flags & 1) != 0,
                X = mode.X, Y = mode.Y, Orientation = mode.Orientation,
                Current = new(mode.Width, mode.Height, mode.Frequency)
            };
            for (uint i = 0; ; i++)
            {
                var adapter = Native.DisplayDevice.Create();
                if (!Native.EnumDisplayDevices(null, i, ref adapter, 0)) break;
                if (adapter.Name == info.Device) { display.Adapter = adapter.Description; break; }
            }
            var edid = ReadEdid(display);
            if (edid is { Length: >= 128 })
            {
                for (int o = 54; o + 18 <= 126; o += 18)
                    if (edid[o] == 0 && edid[o + 1] == 0 && edid[o + 3] == 0xfc)
                        display.Name = System.Text.Encoding.ASCII.GetString(edid, o + 5, 13).Trim('\0', '\n', '\r', ' ');
            }
            var modes = new List<DisplayMode>();
            for (int i = 0; i < 4096; i++)
            {
                var item = Native.DevMode.Create();
                if (!Native.EnumDisplaySettings(info.Device, i, ref item)) break;
                if (item.BitsPerPel == 32) modes.Add(new(item.Width, item.Height, item.Frequency));
            }
            modes.Add(display.Current);
            display.Modes = DisplayRules.NormalizeModes(modes);
            DpiService.Inspect(display);
            WithPhysical(monitor, handle =>
            {
                display.Brightness = ReadPercent(handle, 0x10, display);
                display.Contrast = ReadPercent(handle, 0x12, display);
                display.Volume = ReadPercent(handle, 0x62, display);
                display.HasDdc = display.Brightness.HasValue || display.Contrast.HasValue || display.Volume.HasValue;
                return true;
            });
            result.Add(display);
            return true;
        }, IntPtr.Zero);
        ReadWmiBrightness(result);
        return result.OrderByDescending(d => d.Primary).ThenBy(d => d.Device).Select((d, i) => { d.Number = i + 1; return d; }).ToList();
    }
    private static int? ReadPercent(IntPtr handle, byte code, DisplayInfo display)
    {
        if (!Native.GetVCPFeatureAndVCPFeatureReply(handle, code, out _, out var current, out var max) || max == 0) return null;
        display.VcpMaximums[code] = max;
        return DisplayRules.ToPercent(current, max);
    }
    private static bool WithPhysical(IntPtr monitor, Func<IntPtr, bool> operation)
    {
        if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0 || count > 32) return false;
        var handles = new Native.PhysicalMonitor[count];
        if (!Native.GetPhysicalMonitorsFromHMONITOR(monitor, count, handles)) return false;
        try { return operation(handles[0].Handle); }
        finally { Native.DestroyPhysicalMonitors(count, handles); }
    }
    public Task SetPercentAsync(DisplayInfo display, byte code, int value) => Task.Run(() =>
    {
        lock (gate)
        {
            ValidateIdentity(display.Device, display.Id);
            if (code == 0x10 && display.HasWmi) { SetWmi(display, value); return; }
            bool found = false, ok = false;
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Native.Rect rect, IntPtr data) =>
            {
                var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
                if (Native.GetMonitorInfo(monitor, ref info) && info.Device == display.Device)
                {
                    found = true;
                    ok = WithPhysical(monitor, handle =>
                    {
                        if (!display.VcpMaximums.TryGetValue(code, out var max))
                        {
                            if (!Native.GetVCPFeatureAndVCPFeatureReply(handle, code, out _, out _, out max) || max == 0) return false;
                            display.VcpMaximums[code] = max;
                        }
                        return Native.SetVCPFeature(handle, code, DisplayRules.ToHardware(value, max));
                    });
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (!found || !ok) throw new InvalidOperationException(L.T("sea3ca6fb16"));
        }
    });
    private static void ReadWmiBrightness(List<DisplayInfo> displays)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active=TRUE");
            using var items = searcher.Get();
            foreach (ManagementObject item in items)
            {
                using (item)
                {
                    string instance = Convert.ToString(item["InstanceName"]) ?? "";
                    var normalized = DisplayRules.MonitorInstanceId(instance);
                    var display = displays.FirstOrDefault(d => normalized.Equals(d.Id, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(d.Id + "_", StringComparison.OrdinalIgnoreCase));
                    if (display != null) { display.HasWmi = true; display.WmiInstance = instance; display.Brightness = Convert.ToInt32(item["CurrentBrightness"]); }
                }
            }
        }
        catch (Exception ex) { Storage.Log("WMI brightness detection: " + ex.Message); }
    }
    private static void SetWmi(DisplayInfo display, int value)
    {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
            using var items = searcher.Get();
            bool found = false;
            foreach (ManagementObject item in items)
            {
                using (item) { if ((string)item["InstanceName"] == display.WmiInstance) { uint status = Convert.ToUInt32(item.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)Math.Clamp(value, 0, 100) })); if (status != 0) throw new InvalidOperationException(L.F("sa5a4cdc304", status)); found = true; } }
            }
            if (!found) throw new InvalidOperationException(L.T("s6fe7702098"));
    }
    public static byte[]? ReadEdid(DisplayInfo display)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\" + display.Id.Replace("MONITOR\\", "DISPLAY\\", StringComparison.OrdinalIgnoreCase) + "\\Device Parameters");
            return key?.GetValue("EDID") as byte[];
        }
        catch { return null; }
    }
    public static Native.DevMode CurrentMode(string device)
    {
        var mode = Native.DevMode.Create();
        if (!Native.EnumDisplaySettings(device, -1, ref mode)) throw new InvalidOperationException(L.T("s51258258c0"));
        return mode;
    }
    public static Native.DevMode FindMode(DisplayInfo display, DisplayMode selected, uint orientation)
    {
        if (orientation > 3) throw new InvalidOperationException(L.T("s2f4ed799c1"));
        ValidateIdentity(display.Device, display.Id);
        var original = CurrentMode(display.Device);
        var mode = original;
        bool found = false;
        if (!DisplayRules.Supports(display, selected)) throw new InvalidOperationException(L.T("s94d7c908e4"));
        for (int i = 0; i < 4096; i++)
        {
            var candidate = Native.DevMode.Create();
            if (!Native.EnumDisplaySettings(display.Device, i, ref candidate)) break;
            if (candidate.Width == selected.Width && candidate.Height == selected.Height && candidate.Frequency == selected.Frequency && candidate.BitsPerPel == 32) { mode = candidate; found = true; break; }
        }
        if (!found) throw new InvalidOperationException(L.T("sfa7b26c529"));
        mode.X = original.X; mode.Y = original.Y;
        if ((orientation % 2) != (mode.Orientation % 2)) (mode.Width, mode.Height) = (mode.Height, mode.Width);
        mode.Orientation = orientation;
        mode.Fields = 0x20 | 0x80 | 0x80000 | 0x100000 | 0x800000 | 0x40000;
        return mode;
    }
    public static void ValidateIdentity(string deviceName, string expectedId)
    {
        var device = Native.DisplayDevice.Create();
        if (!Native.EnumDisplayDevices(deviceName, 0, ref device, 1) || !DisplayRules.MonitorInstanceId(device.Id).Equals(expectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("s9a4e077006"));
    }
    public static void ApplyMode(string device, Native.DevMode mode, bool test = false, string? expectedId = null)
    {
        if (expectedId != null) ValidateIdentity(device, expectedId);
        int result = Native.ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, test ? 2u : 0u, IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException(L.F("sed8e561bc7", result));
    }
}
