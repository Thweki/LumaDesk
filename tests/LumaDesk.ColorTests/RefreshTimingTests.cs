// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class RefreshTimingTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(Marshal.SizeOf<RefreshTimingNative.Mode>() == 64 &&
            Marshal.OffsetOf<RefreshTimingNative.Mode>(nameof(RefreshTimingNative.Mode.VerticalSync)).ToInt32() == 32,
            "Refresh timing uses the native target-mode layout");
        foreach (string? value in new[] { null, "", "0", "true", "yes", " 1" })
            check(!RefreshTimingService.FlagEnabled(value), "Timing experiment requires explicit opt-in: " + value);
        check(RefreshTimingService.FlagEnabled("1"), "Timing experiment accepts explicit opt-in");
        var backend = new FakeBackend();
        var display = new DisplayInfo { Id = "fixture", Device = @"\\.\DISPLAY2" };
        RefreshTiming Read() => RefreshTimingService.Read(display, true, backend);
        var disabled = RefreshTimingService.Read(display, false, backend);
        check(disabled.Error.Length > 0 && backend.Validations == 0 && backend.Flags.Count == 0,
            "Disabled experiment does not touch hardware");
        var timing = Read();
        check(timing.Error == "" && Math.Abs(timing.DesktopHz!.Value - 60000d / 1001) < 0.000001 && timing.SignalHz == 120,
            "Selected source retains fractional desktop rate and separate physical rate");
        check(timing.DynamicRefreshEnabled == true && backend.Validations == 2 && backend.Flags.SequenceEqual(new uint[] { 0x52 }),
            "DRR comes from refresh-aware flags with identity validation before and after");
        backend.Path.Flags = 9;
        check(Read().DynamicRefreshEnabled == false, "Virtual-mode support does not imply DRR enabled");
        foreach (int error in new[] { 87, 50 })
        {
            backend.FirstError = error; backend.Flags.Clear();
            timing = Read();
            check(timing.DynamicRefreshEnabled == null && timing.SignalHz == 120 && backend.Flags.SequenceEqual(new uint[] { 0x52, 0x12 }),
                "Unsupported awareness falls back without claiming DRR is disabled: " + error);
        }
        backend.FirstError = 5; backend.Flags.Clear();
        timing = Read();
        check(timing.Error.Length > 0 && timing.DesktopHz == null && timing.DynamicRefreshEnabled == null && backend.Flags.Count == 1,
            "Access denied is an error, not a capability fallback or disabled status");
        backend.FirstError = 0;
        backend.Duplicate = true;
        check(Read().Error.Length > 0, "Cloned source is not attributed to an arbitrary target");
        backend.Duplicate = false;
        backend.Name = @"\\.\DISPLAY3";
        check(Read().Error.Length > 0, "Missing selected source cannot borrow another display's timing");
        backend.Name = display.Device.ToLowerInvariant();
        check(Read().SignalHz == 120, "GDI source matching is case insensitive");
        backend.FailValidationAt = backend.Validations + 2;
        check(Read().Error.Length > 0, "Hotplug identity change discards the snapshot");
        backend.FailValidationAt = 0;

        var path = backend.Path;
        path.Flags = 1; path.Target.ModeIndex = 1;
        check(RefreshTimingService.Decode(path, backend.Modes, true).SignalHz == 120, "Legacy path uses the full target mode index");
        path.Flags = 9; path.Target.ModeIndex = 0x00010000;
        check(RefreshTimingService.Decode(path, backend.Modes, true).SignalHz == 120, "Virtual path uses the high index, not the desktop-image low index");
        foreach (uint index in new[] { 0xffff0000u, 0x00020000u, uint.MaxValue })
        {
            path.Target.ModeIndex = index;
            check(RefreshTimingService.Decode(path, backend.Modes, true).SignalHz == null, "Missing target mode remains unknown: " + index);
        }
        path = backend.Path;
        var good = backend.Modes[1];
        backend.Modes[1].Id++;
        check(Read().SignalHz == null, "Wrong target ID cannot supply timing");
        backend.Modes[1] = good; backend.Modes[1].Adapter.High++;
        check(Read().SignalHz == null, "Wrong adapter cannot supply timing");
        backend.Modes[1] = good; backend.Modes[1].Type = 1;
        check(Read().SignalHz == null, "Source-mode union is never interpreted as target timing");
        backend.Modes[1] = good; backend.Modes[1].VerticalSync.Denominator = 0;
        backend.Path.Target.Refresh.Numerator = 0;
        check(Read().SignalHz == null && Read().DesktopHz == null, "Zero numerator or denominator is unknown, never zero Hz or infinity");
        path.Target.Available = 0;
        check(RefreshTimingService.Decode(path, backend.Modes, true).Error.Length > 0, "Unavailable target is not shown as live");
        path.Target.Available = 1; path.Flags = 8;
        check(RefreshTimingService.Decode(path, backend.Modes, true).Error.Length > 0, "Inactive path is not shown as live");
    }

    private sealed class FakeBackend : IRefreshTimingBackend
    {
        internal DpiNative.Path Path = new()
        {
            Flags = 0x19,
            Target = new() { Id = 7, Available = 1, Adapter = new() { Low = 10, High = -1 },
                ModeIndex = 0x00010000, Refresh = new() { Numerator = 60000, Denominator = 1001 } }
        };
        internal RefreshTimingNative.Mode[] Modes = new RefreshTimingNative.Mode[]
        {
            new() { Type = 3 },
            new() { Type = 2, Id = 7, Adapter = new() { Low = 10, High = -1 }, VerticalSync = new() { Numerator = 120, Denominator = 1 } }
        };
        internal string Name = @"\\.\DISPLAY2";
        internal int Validations, FailValidationAt, FirstError;
        internal bool Duplicate;
        internal List<uint> Flags = new();
        public void ValidateIdentity(DisplayInfo display)
        {
            if (++Validations == FailValidationAt) throw new InvalidOperationException("Disconnected fixture");
        }
        public string SourceName(DpiNative.Source source) => Name;
        public (DpiNative.Path[] Paths, RefreshTimingNative.Mode[] Modes) Query(uint flags)
        {
            Flags.Add(flags);
            if ((flags & 0x40) != 0 && FirstError != 0) throw new Win32Exception(FirstError);
            return (Duplicate ? new[] { Path, Path } : new[] { Path }, Modes);
        }
    }
}
