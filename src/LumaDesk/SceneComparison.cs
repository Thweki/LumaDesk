// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LumaDesk;

internal sealed record SceneDifference(string DisplayId, string DisplayName, string Setting, string Current, string Saved);

// A comparison of snapshots, not a promise that the driver can apply the saved values.
internal sealed class SceneComparison
{
    internal List<SceneDifference> Differences { get; } = new();
    internal List<string> MissingDisplays { get; } = new();
    internal List<string> ExtraDisplays { get; } = new();

    internal static SceneComparison Compare(Preset saved, Preset current)
    {
        var result = new SceneComparison();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in saved.Displays)
        {
            if (!ids.Add(target.Id)) continue;
            var now = current.Displays.FirstOrDefault(d => string.Equals(d.Id, target.Id, StringComparison.OrdinalIgnoreCase));
            if (now == null) { result.MissingDisplays.Add(Name(target)); continue; }
            void Add(string setting, object? before, object? after, string suffix = "")
            {
                // Legacy presets leave fields unspecified; unknown readings remain visible.
                if (after == null || Equals(before, after)) return;
                result.Differences.Add(new(target.Id, Name(now), setting, Format(before, suffix), Format(after, suffix)));
            }
            Add("scene.brightness", now.Brightness, target.Brightness, "%");
            Add("s8fb81ba8f3", now.Dimming, target.Dimming, "%");
            Add("scene.mode", now.Mode, target.Mode);
            if (target.Mode != null) Add("s60cc99cc00", Degrees(now.Orientation), Degrees(target.Orientation), "°");
            Add("scene.scale", now.Dpi, target.Dpi, "%");
            Add("HDR", now.Hdr, target.Hdr);
            Add("compare.userProfiles", now.UserProfiles, target.UserProfiles);
            if (target.UserProfiles == true)
            {
                void Profile(string setting, string? before, string? after)
                {
                    if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) Add(setting, before, after);
                }
                Profile("s934a4fdc56", now.SdrProfile, target.SdrProfile);
                Profile("s98a58b2d5c", now.HdrProfile, target.HdrProfile);
            }
            Add("compare.x", now.X, target.X);
            Add("compare.y", now.Y, target.Y);
            Add("compare.primary", now.Primary, target.Primary);
        }
        result.ExtraDisplays.AddRange(current.Displays.Where(d => !ids.Contains(d.Id)).Select(Name));
        return result;
    }

    private static string Name(PresetDisplay display) => string.IsNullOrEmpty(display.Name) ? display.Id : display.Name;
    private static long? Degrees(uint? orientation) => orientation.HasValue ? (long)orientation.Value * 90 : null;
    private static string Format(object? value, string suffix) => value switch
    {
        null => L.T("s4d8c1c5b42"),
        bool enabled => L.T(enabled ? "compare.yes" : "compare.no"),
        "" => L.T("s57098ba8c4"),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) + suffix
    };
}
