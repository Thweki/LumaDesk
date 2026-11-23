// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

internal sealed record DisplayInventoryReport(int SchemaVersion, DateTimeOffset CapturedAtUtc, InventoryDisplay[] Displays);
internal sealed record InventoryDisplay(
    int Number, string Id, string Device, string Name, string Adapter, bool Primary,
    int X, int Y, uint RotationDegrees, DisplayMode Current,
    uint PreferredWidth, uint PreferredHeight, int? ScalePercent, string ScaleUnavailableReason,
    int? HardwareBrightnessPercent, int? ContrastPercent, int? VolumePercent, InventoryColor Color);
internal sealed record InventoryColor(
    bool? HdrSupported, bool? HdrUserEnabled, bool? HdrActive, bool? PolicyLimited,
    uint? BitsPerChannel, string Encoding, double? SdrWhiteNits, string HdrUnavailableReason,
    bool? UseUserProfiles, string? SdrProfile, string? HdrProfile, string ProfileUnavailableReason);

internal static class DisplayInventory
{
    // Always take a fresh hardware snapshot, independent of the tray's cached state and aliases.
    // The injected operations are reads only; no scene/apply or preferences backend is needed.
    internal static async Task<DisplayInventoryReport> CaptureAsync(
        Func<Task<List<DisplayInfo>>> scan, Func<DisplayInfo, DisplayColorState> readColor)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var displays = await scan();
        return await Task.Run(() => new DisplayInventoryReport(1, capturedAt,
            displays.Select(display => Describe(display, readColor)).ToArray()));
    }

    private static InventoryDisplay Describe(DisplayInfo display, Func<DisplayInfo, DisplayColorState> readColor)
    {
        DisplayColorState color;
        try { color = readColor(display); }
        catch (Exception ex)
        {
            // A disconnected screen or unavailable color API must not discard the other screens.
            color = new() { HdrUnavailableReason = ex.Message, ProfileUnavailableReason = ex.Message };
        }
        return new(display.Number, display.Id, display.Device, display.Name, display.Adapter, display.Primary,
            display.X, display.Y, display.Orientation * 90, display.Current,
            display.PreferredWidth, display.PreferredHeight,
            display.DpiScale.CurrentPercent > 0 ? display.DpiScale.CurrentPercent : null, display.DpiScale.UnavailableReason,
            display.Brightness, display.Contrast, display.Volume,
            new(color.HdrKnown ? color.HdrSupported : null, color.HdrKnown ? color.HdrUserEnabled : null,
                color.HdrKnown ? color.HdrActive : null, color.HdrKnown ? color.PolicyLimited : null,
                color.BitsPerChannel > 0 ? color.BitsPerChannel : null, color.Encoding, color.SdrWhiteNits, color.HdrUnavailableReason,
                color.ProfilesAvailable ? color.UseUserProfiles : null,
                color.ProfilesAvailable ? color.SdrProfile : null, color.ProfilesAvailable ? color.HdrProfile : null,
                color.ProfileUnavailableReason));
    }
}
