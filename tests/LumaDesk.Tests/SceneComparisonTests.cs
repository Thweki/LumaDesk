// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneComparisonTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(!new Preferences().ExperimentalPresetComparison &&
            !JsonSerializer.Deserialize<Preferences>("{}")!.ExperimentalPresetComparison,
            "Preset comparison defaults off for new and legacy settings");
        var preferences = new Preferences { ExperimentalPresetComparison = true };
        check(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!.ExperimentalPresetComparison,
            "Experimental comparison opt-in survives a settings roundtrip");
        var saved = new Preset { Displays = new() { new()
        {
            Id = "A", Name = "Saved name", Brightness = 25, Dimming = 10.5, Mode = new(1920, 1080, 60),
            Orientation = 1, Dpi = 150, Hdr = true, UserProfiles = true, SdrProfile = "sRGB.icm",
            HdrProfile = "HDR.icm", X = -1920, Y = 0, Primary = true
        } } };
        var current = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(saved))!;
        current.Displays[0].Id = "a";
        current.Displays[0].Name = "Current alias";
        current.Displays[0].SdrProfile = "SRGB.ICM";
        var report = SceneComparison.Compare(saved, current);
        check(report.Differences.Count == 0 && report.MissingDisplays.Count == 0 && report.ExtraDisplays.Count == 0,
            "Comparison matches stable IDs and profile names ignoring case, independent of aliases");
        var now = current.Displays[0];
        now.Brightness = 80; now.Dimming = 0; now.Mode = new(2560, 1440, 144); now.Orientation = 0;
        now.Dpi = 100; now.Hdr = false; now.UserProfiles = false; now.SdrProfile = "other.icm";
        now.HdrProfile = ""; now.X = 0; now.Y = 1080; now.Primary = false;
        string beforeSaved = JsonSerializer.Serialize(saved), beforeCurrent = JsonSerializer.Serialize(current);
        report = SceneComparison.Compare(saved, current);
        check(report.Differences.Count == 13 && report.Differences.All(d => d.DisplayName == "Current alias"),
            "Comparison reports all thirteen changed display fields using current aliases");
        check(report.Differences.Single(d => d.Setting == "scene.brightness") is { Current: "80%", Saved: "25%" } &&
            report.Differences.Single(d => d.Setting == "s60cc99cc00") is { Current: "0°", Saved: "90°" },
            "Comparison distinguishes current from saved values and formats rotation as degrees");
        check(JsonSerializer.Serialize(saved) == beforeSaved && JsonSerializer.Serialize(current) == beforeCurrent,
            "Comparison never mutates either snapshot or preset identity");
        now.Brightness = null; now.Dpi = null; now.Hdr = null; now.UserProfiles = null; now.SdrProfile = null;
        report = SceneComparison.Compare(saved, current);
        foreach (string setting in new[] { "scene.brightness", "scene.scale", "HDR", "compare.userProfiles", "s934a4fdc56" })
            check(report.Differences.Single(d => d.Setting == setting).Current == L.T("s4d8c1c5b42"),
                "Unreadable current values remain unknown: " + setting);
        saved.Displays[0].SdrProfile = "";
        report = SceneComparison.Compare(saved, current);
        check(report.Differences.Single(d => d.Setting == "s934a4fdc56").Saved == L.T("s57098ba8c4"),
            "Explicit empty profile is distinct from an unreadable current profile");
        saved.Displays[0].UserProfiles = false;
        report = SceneComparison.Compare(saved, current);
        check(!report.Differences.Any(d => d.Setting is "s934a4fdc56" or "s98a58b2d5c"),
            "System profile scope does not promise personal profile assignments");
        var legacy = JsonSerializer.Deserialize<Preset>("{\"Displays\":[{\"Id\":\"A\",\"Brightness\":20,\"Dimming\":5}]}")!;
        report = SceneComparison.Compare(legacy, current);
        check(report.Differences.Select(d => d.Setting).SequenceEqual(new[] { "scene.brightness", "s8fb81ba8f3" }),
            "Legacy brightness presets do not invent mode, color, scaling or layout changes");
        legacy.Displays.Add(new() { Id = "gone", Name = "Disconnected" });
        current.Displays.Add(new() { Id = "extra", Name = "Extra" });
        legacy.Displays.Add(legacy.Displays[0]);
        report = SceneComparison.Compare(legacy, current);
        check(report.MissingDisplays.SequenceEqual(new[] { "Disconnected" }) && report.ExtraDisplays.SequenceEqual(new[] { "Extra" }) && report.Differences.Count == 2,
            "Comparison reports missing and extra displays and deduplicates saved IDs like scene apply");
        check(SceneComparison.Compare(new Preset(), new Preset()).Differences.Count == 0,
            "Empty snapshots can be compared without exceptions");
    }
}
