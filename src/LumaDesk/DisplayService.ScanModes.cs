// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDesk;

internal sealed partial class DisplayService
{
    private static uint? ReadScanMode(IntPtr handle) =>
        Native.GetVCPFeatureAndVCPFeatureReply(handle, ScanModeRules.VcpCode, out _, out uint current, out _) ? current : null;

    private static MonitorScanModes InspectScanModes(IntPtr handle)
    {
        string capabilities = "";
        if (GetCapabilitiesStringLength(handle, out uint length) && length > 0 && length <= 65536)
        {
            var buffer = new StringBuilder((int)length);
            if (CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length)) capabilities = buffer.ToString();
        }
        // Do not probe DA on monitors that do not advertise known scan modes.
        return ScanModeRules.Parse(capabilities).Length == 0
            ? new(null, Array.Empty<MonitorScanMode>()) : ScanModeRules.Resolve(ReadScanMode(handle), capabilities);
    }

    internal Task<MonitorScanModes> ReadScanModesAsync(DisplayInfo display) => Task.Run(() =>
    {
        var result = new MonitorScanModes(null, Array.Empty<MonitorScanMode>());
        if (!ScanModeRules.Enabled) return result;
        lock (Gate(display))
            WithDisplay(display, handle => { result = InspectScanModes(handle); return true; });
        return result;
    });

    internal async Task<MonitorScanModes> SetScanModeAsync(DisplayInfo display, uint code)
    {
        ScanModeRules.RequireEnabled(ScanModeRules.Enabled);
        Interlocked.Increment(ref activeWrites);
        try
        {
            return await Task.Run(() =>
            {
                lock (Gate(display))
                {
                    ScanModeRules.RequireEnabled(ScanModeRules.Enabled);
                    MonitorScanModes? result = null;
                    bool ok = WithDisplay(display, handle =>
                    {
                        result = ScanModeRules.Apply(ScanModeRules.Enabled, code, () => InspectScanModes(handle),
                            value => Native.SetVCPFeature(handle, ScanModeRules.VcpCode, value), () => ReadScanMode(handle));
                        return true;
                    });
                    return ok && result != null ? result : throw new InvalidOperationException(L.T("scan.unavailable"));
                }
            });
        }
        finally { Interlocked.Decrement(ref activeWrites); }
    }
}
