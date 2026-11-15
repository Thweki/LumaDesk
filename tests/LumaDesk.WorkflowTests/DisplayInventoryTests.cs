// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.IO;
using System.Text.Json;
using LumaDesk;

internal static class DisplayInventoryTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var parsed = CommandService.Parse(new[] { "--inventory", "--output", "inventory.json" });
        check(CommandService.IsCommand(new[] { "--inventory" }) && parsed.Command == new ControlCommand("inventory") &&
            Path.IsPathFullyQualified(parsed.Output), "Inventory CLI is recognized and resolves an explicit output file");
        foreach (var invalid in new[]
        {
            new[] { "--inventory" }, new[] { "--inventory", "--output", " " },
            new[] { "--inventory", "A", "--output", "r.json" },
            new[] { "--inventory", "--output", "r.json", "extra" },
            new[] { "--inventory", "--brightness", "A", "50", "--output", "r.json" }
        })
        {
            bool rejected = false;
            try { CommandService.Parse(invalid); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Inventory rejects missing output and extra or mutating arguments");
        }

        var screens = new List<DisplayInfo>
        {
            new() { Id = "A", Number = 1, Device = @"\\.\DISPLAY1", Name = "屏幕", Adapter = "GPU", Primary = true,
                X = -1440, Y = -120, Orientation = 1, Current = new(1440, 2560, 144),
                PreferredWidth = 2560, PreferredHeight = 1440, DpiScale = DpiRules.FromRelative(-2, 0, 3),
                Brightness = 75, Contrast = 50, Volume = 0, Dimming = 30, RegistryPath = "internal registry path", WmiInstance = "internal WMI path" },
            new() { Id = "B", Number = 2, Name = "Unavailable" },
            new() { Id = "C", Number = 3, Name = "SDR" }
        };
        var color = new DisplayColorState
        {
            HdrKnown = true, HdrSupported = true, HdrUserEnabled = true, HdrActive = true,
            BitsPerChannel = 10, Encoding = "RGB", SdrWhiteNits = 200,
            ProfilesAvailable = true, UseUserProfiles = true, SdrProfile = "sRGB.icm", HdrProfile = "HDR.icc"
        };
        string before = JsonSerializer.Serialize(screens, Storage.Json);
        int scans = 0;
        var colorReads = new List<string>();
        var started = DateTimeOffset.UtcNow;
        var report = await DisplayInventory.CaptureAsync(() => { scans++; return Task.FromResult(screens); }, display =>
        {
            colorReads.Add(display.Id);
            if (display.Id == "B") throw new InvalidOperationException("Display disconnected");
            return display.Id == "A" ? color : new() { HdrKnown = true, ProfilesAvailable = true };
        });
        check(scans == 1 && colorReads.SequenceEqual(new[] { "A", "B", "C" }), "Inventory scans once and reads color for every screen despite a failed read");
        check(report.SchemaVersion == 1 && report.CapturedAtUtc >= started && report.CapturedAtUtc <= DateTimeOffset.UtcNow &&
            report.CapturedAtUtc.Offset == TimeSpan.Zero, "Inventory carries a schema version and UTC capture time");
        var first = report.Displays[0];
        check(first.Id == "A" && first.Device == @"\\.\DISPLAY1" && first.Name == "屏幕" && first.Adapter == "GPU" && first.Primary &&
            first.X == -1440 && first.Y == -120 && first.RotationDegrees == 90 && first.Current == new DisplayMode(1440, 2560, 144) &&
            first.PreferredWidth == 2560 && first.PreferredHeight == 1440 && first.ScalePercent == 150,
            "Inventory preserves identity, negative coordinates, rotation, mode, preferred pixels and scaling");
        check(first.HardwareBrightnessPercent == 75 && first.ContrastPercent == 50 && first.VolumePercent == 0 &&
            first.Color.HdrSupported == true && first.Color.HdrActive == true && first.Color.HdrUserEnabled == true &&
            first.Color.BitsPerChannel == 10 && first.Color.Encoding == "RGB" && first.Color.SdrWhiteNits == 200 &&
            first.Color.UseUserProfiles == true && first.Color.SdrProfile == "sRGB.icm" && first.Color.HdrProfile == "HDR.icc",
            "Inventory includes hardware controls, HDR signal and assigned color profiles");
        var unavailable = report.Displays[1];
        check(unavailable.ScalePercent == null && unavailable.HardwareBrightnessPercent == null &&
            unavailable.Color.HdrSupported == null && unavailable.Color.HdrActive == null && unavailable.Color.BitsPerChannel == null &&
            unavailable.Color.UseUserProfiles == null && unavailable.Color.SdrProfile == null &&
            unavailable.Color.HdrUnavailableReason == "Display disconnected" && unavailable.Color.ProfileUnavailableReason == "Display disconnected",
            "Failed optional reads retain the screen and report unknown values with reasons");
        check(report.Displays[2].Color.HdrSupported == false && report.Displays[2].Color.HdrActive == false &&
            report.Displays[2].Color.UseUserProfiles == false && report.Displays[2].Color.SdrProfile == "",
            "Known unsupported HDR and absent profiles remain distinct from unknown results");
        check(JsonSerializer.Serialize(screens, Storage.Json) == before, "Inventory does not mutate display state");
        screens[0].Name = "Changed"; color.SdrProfile = "Changed.icm";
        check(first.Name == "屏幕" && first.Color.SdrProfile == "sRGB.icm", "Inventory is a snapshot independent of later display and color changes");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new ControlReply(true, report), Storage.Json));
        var data = json.RootElement.GetProperty("Data");
        var items = data.GetProperty("Displays");
        check(json.RootElement.GetProperty("Success").GetBoolean() && data.GetProperty("SchemaVersion").GetInt32() == 1 &&
            items.GetArrayLength() == 3 && items[0].GetProperty("Name").GetString() == "屏幕" &&
            items[1].GetProperty("Color").GetProperty("HdrSupported").ValueKind == JsonValueKind.Null &&
            items[2].GetProperty("Color").GetProperty("HdrSupported").ValueKind == JsonValueKind.False,
            "Inventory JSON uses the CLI envelope and preserves Unicode and nullable capability semantics");
        check(!items[0].TryGetProperty("RegistryPath", out _) && !items[0].TryGetProperty("WmiInstance", out _) &&
            !items[0].TryGetProperty("Dimming", out _), "Inventory excludes internal paths and unobserved software dimming");
        var legacyColor = await DisplayInventory.CaptureAsync(() => Task.FromResult(screens), _ => new()
        {
            BitsPerChannel = 8, Encoding = "RGB", HdrUnavailableReason = "Legacy API",
            SdrProfile = "Partial.icm", ProfileUnavailableReason = "Profile query failed"
        });
        check(legacyColor.Displays.All(d => d.Color.HdrSupported == null && d.Color.BitsPerChannel == 8 &&
            d.Color.HdrUnavailableReason == "Legacy API" && d.Color.SdrProfile == null &&
            d.Color.ProfileUnavailableReason == "Profile query failed"),
            "Partial color reads preserve signal data without claiming HDR support or complete profile results");
        var empty = await DisplayInventory.CaptureAsync(() => Task.FromResult(new List<DisplayInfo>()), _ => throw new Exception("Unexpected color read"));
        check(empty.Displays.Length == 0, "No active displays produces an empty inventory");
        bool scanFailed = false;
        try { await DisplayInventory.CaptureAsync(() => Task.FromException<List<DisplayInfo>>(new InvalidOperationException("Scan failed")), _ => color); }
        catch (InvalidOperationException ex) { scanFailed = ex.Message == "Scan failed"; }
        check(scanFailed, "A failed scan is not reported as a successful empty inventory");
    }
}
