// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace LumaDesk;

internal interface IGammaOperations
{
    DisplayColorState Color(DisplayInfo display);
    ushort[] Read(DisplayInfo display);
    void Write(DisplayInfo display, ushort[] ramp);
}

internal static class GammaService
{
    // Session opt-in, with no persisted preference or startup/reconnect writes.
    internal static bool Enabled => IsEnabled(Environment.GetEnvironmentVariable("LUMADESK_EXPERIMENTAL_GAMMA"));
    internal static bool IsEnabled(string? value) => value == "1";

    internal static void Validate(ushort[] ramp)
    {
        if (ramp.Length != 768) throw new InvalidOperationException(L.T("gamma.invalid"));
        for (int channel = 0; channel < 3; channel++)
        {
            int start = channel * 256;
            if (ramp[start] >= ramp[start + 255]) throw new InvalidOperationException(L.T("gamma.invalid"));
            for (int i = start + 1; i < start + 256; i++)
                if (ramp[i] < ramp[i - 1]) throw new InvalidOperationException(L.T("gamma.invalid"));
        }
    }

    private static bool IsSdr(DisplayColorState state) => state.HdrKnown && state.ColorMode == "SDR" && !state.HdrActive && !state.HdrUserEnabled;

    internal static ushort[]? Capture(DisplayInfo display, IGammaOperations? operations = null)
    {
        operations ??= new NativeGammaOperations();
        if (!IsSdr(operations.Color(display))) return null;
        var ramp = operations.Read(display);
        Validate(ramp);
        return ramp.ToArray();
    }

    internal static void Restore(DisplayInfo display, ushort[] ramp, IGammaOperations? operations = null)
    {
        Validate(ramp);
        operations ??= new NativeGammaOperations();
        // Read current color state after preset HDR/ICC/layout changes, never trust saved HDR.
        if (!IsSdr(operations.Color(display))) throw new InvalidOperationException(L.T("gamma.sdrOnly"));
        var target = ramp.ToArray();
        if (operations.Read(display).SequenceEqual(target)) return;
        operations.Write(display, target);
        // Windows can return success while ignoring a ramp. Never retry blindly.
        if (!operations.Read(display).SequenceEqual(target)) throw new InvalidOperationException(L.T("gamma.unverified"));
    }

    private sealed class NativeGammaOperations : IGammaOperations
    {
        public DisplayColorState Color(DisplayInfo display) => AdvancedColorService.Read(display);

        private static void WithDc(DisplayInfo display, Action<IntPtr> action)
        {
            // Validates the stable device ID and rejects mirrored/ambiguous paths.
            // Revalidate on every access; never fall back to the desktop DC.
            _ = AdvancedColorService.FindPath(display);
            IntPtr dc = CreateDCW("DISPLAY", display.Device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new InvalidOperationException(L.T("gamma.unavailable"));
            try { action(dc); }
            finally { DeleteDC(dc); }
        }

        public ushort[] Read(DisplayInfo display)
        {
            var ramp = new ushort[768];
            WithDc(display, dc =>
            {
                if (!GetDeviceGammaRamp(dc, ramp)) throw new InvalidOperationException(L.T("gamma.unavailable"));
            });
            return ramp;
        }

        public void Write(DisplayInfo display, ushort[] ramp) => WithDc(display, dc =>
        {
            if (!SetDeviceGammaRamp(dc, ramp)) throw new InvalidOperationException(L.T("gamma.unavailable"));
        });

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr CreateDCW(string driver, string device, string? output, IntPtr initData);
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDeviceGammaRamp(IntPtr dc, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 768)] ushort[] ramp);
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDeviceGammaRamp(IntPtr dc, [In, MarshalAs(UnmanagedType.LPArray, SizeConst = 768)] ushort[] ramp);
    }
}
