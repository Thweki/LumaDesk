// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LumaDesk;

// Owned by one tray panel; all calls originate on its dispatcher.
internal sealed class PanelKeepAwake : IDisposable
{
    internal const string Flag = "LUMADESK_EXPERIMENTAL_KEEP_AWAKE";
    internal static bool IsEnabled(string? flag) => flag == "1";
    internal bool Available { get; }
    internal bool Active => request != null;
    private readonly Func<IDisposable> acquire;
    private IDisposable? request;
    private bool disposed;

    internal PanelKeepAwake(bool available, Func<IDisposable> acquire)
    {
        Available = available;
        this.acquire = acquire;
    }

    internal void SetEnabled(bool enabled, bool panelVisible)
    {
        if (disposed) return;
        if (Available && enabled && panelVisible)
        {
            // Publish active state only after Windows accepts the request.
            request ??= acquire();
        }
        else
        {
            request?.Dispose();
            request = null;
        }
    }

    public void Dispose()
    {
        SetEnabled(false, false);
        disposed = true;
    }
}

internal static class DisplayWakeRequest
{
    // Preserve the full REASON_CONTEXT union layout on both x86 and x64.
    [StructLayout(LayoutKind.Sequential)]
    internal struct ReasonContext { internal uint Version, Flags; internal ReasonUnion Reason; }
    [StructLayout(LayoutKind.Explicit)]
    internal struct ReasonUnion
    {
        [FieldOffset(0)] internal IntPtr SimpleReasonString;
        [FieldOffset(0)] internal DetailedReason Detailed;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct DetailedReason { internal IntPtr Module; internal uint Id, Count; internal IntPtr Strings; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(SafeFileHandle request, int type);

    internal static IDisposable Acquire()
    {
        var reason = Marshal.StringToHGlobalUni(L.T("awake.reason"));
        try
        {
            var context = new ReasonContext { Version = 0, Flags = 1, Reason = new ReasonUnion { SimpleReasonString = reason } };
            var handle = PowerCreateRequest(ref context);
            try
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                // DisplayRequired (0) needs SystemRequired (1) to also prevent idle system sleep.
                if (!PowerSetRequest(handle, 0) || !PowerSetRequest(handle, 1))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                // Closing the owned handle cleans up the entire request, including partial failure.
                // SafeFileHandle also provides finalization if a window cannot complete cleanup.
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
        finally { Marshal.FreeHGlobal(reason); }
    }
}
