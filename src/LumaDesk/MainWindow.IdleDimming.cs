// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LumaDesk;

public partial class MainWindow
{
    private readonly IdleDimming idleDimming = new();
    private readonly DispatcherTimer idleDimmingTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void StartIdleDimming()
    {
        idleDimmingTimer.Tick += (_, _) => TickIdleDimming();
        ConfigureIdleDimming();
    }

    private void ConfigureIdleDimming()
    {
        idleDimmingTimer.Stop();
        ResetIdleDimming();
        if (Preferences.ExperimentalIdleDimming && !testing) idleDimmingTimer.Start();
    }

    private void TickIdleDimming()
    {
        if (scanning || sceneBusy || modePending || restoringConnection || brightnessGates.Values.Any(gate => gate.CurrentCount == 0))
        {
            ResetIdleDimming();
            return;
        }
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        uint? idle = GetLastInputInfo(ref input)
            ? IdleDimming.IdleMilliseconds(unchecked((uint)Environment.TickCount), input.Time) : null;
        if (!idleDimming.Update(Preferences.ExperimentalIdleDimming, Preferences.IdleDimmingMinutes, idle, Environment.TickCount64)) return;
        try
        {
            foreach (var display in Displays)
            {
                if (idleDimming.IsDimmed) DisplayService.ValidateIdentity(display.Device, display.Id);
                RenderDimming(display, idleDimming.EffectiveDimming(display.Dimming));
            }
        }
        catch (Exception ex)
        {
            // Remove even partially applied temporary shades; retry only after a new grace period.
            ResetIdleDimming();
            Storage.Log("Idle dimming: " + ex.Message);
        }
    }

    private void ResetIdleDimming()
    {
        bool restore = idleDimming.IsDimmed;
        idleDimming.Reset();
        if (restore)
            foreach (var display in Displays) RenderDimming(display, display.Dimming);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { internal uint Size, Time; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
