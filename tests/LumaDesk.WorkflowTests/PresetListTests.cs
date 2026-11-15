// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors
using System.IO;
using System.Text.Json;
using LumaDesk;

internal static class PresetListTests
{
    internal static void Run(Action<bool, string> check)
    {
        var args = new[] { "--list-presets", "--output", "presets.json" };
        check(CommandService.IsCommand(args), "Preset discovery is routed to the command client at startup");
        var parsed = CommandService.Parse(args);
        check(parsed.Command == new ControlCommand("list-presets") && parsed.Output == Path.GetFullPath("presets.json"),
            "Preset discovery accepts the standard result-file contract");
        foreach (var invalid in new[]
        {
            new[] { "--list-presets" },
            new[] { "--list-presets", "--output" },
            new[] { "--list-presets", "--output", " " },
            new[] { "--list-presets", "unexpected", "--output", "presets.json" },
            new[] { "--list-presets", "--output", "presets.json", "extra" }
        })
        {
            bool rejected = false;
            try { CommandService.Parse(invalid); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Preset discovery rejects malformed arguments: " + string.Join(" ", invalid));
        }

        using var empty = JsonDocument.Parse(JsonSerializer.Serialize(CommandService.ListPresets(new Preferences()), Storage.Json));
        check(empty.RootElement.GetProperty("Success").GetBoolean() && empty.RootElement.GetProperty("Data").GetArrayLength() == 0,
            "An empty preset library is a successful empty array");

        var preferences = new Preferences
        {
            Presets = new()
            {
                new() { Id = "work-id", Name = "办公 \"Work\"", OnConnect = true, AtTime = "09:00",
                    Displays = new() { new() { Id = @"DISPLAY\A", Name = "Desk", Brightness = 60, SdrProfile = "private.icm" },
                        new() { Id = @"DISPLAY\B", Name = "Disconnected screen" } } },
                new() { Id = "other-id", Name = "办公 \"Work\"" }
            }
        };
        string before = JsonSerializer.Serialize(preferences, Storage.Json);
        var reply = CommandService.ListPresets(preferences);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(reply, Storage.Json));
        var entries = json.RootElement.GetProperty("Data");
        check(reply.Success && reply.Error == null && entries.GetArrayLength() == 2 &&
            entries[0].GetProperty("Id").GetString() == "work-id" && entries[1].GetProperty("Id").GetString() == "other-id" &&
            entries[0].GetProperty("Name").GetString() == preferences.Presets[0].Name,
            "Preset discovery preserves order, Unicode names and distinct IDs even when names repeat");
        var displays = entries[0].GetProperty("Displays");
        check(displays.GetArrayLength() == 2 && displays[0].GetProperty("Id").GetString() == @"DISPLAY\A" &&
            displays[1].GetProperty("Name").GetString() == "Disconnected screen" && entries[1].GetProperty("Displays").GetArrayLength() == 0,
            "Preset discovery includes saved display identities without requiring connected hardware");
        check(entries[0].EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "Id", "Name", "Displays" }) &&
            displays[0].EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "Id", "Name" }),
            "Preset discovery returns a summary without saved control values or automation settings");
        check(JsonSerializer.Serialize(preferences, Storage.Json) == before, "Listing presets leaves preferences unchanged");
        check(CommandService.Parse(new[] { "--apply-preset", entries[0].GetProperty("Id").GetString()!, "--output", "apply.json" }).Command ==
            new ControlCommand("preset", "work-id"), "Discovered IDs can be passed directly to apply-preset");

        string? originalFolder = Environment.GetEnvironmentVariable("LUMADESK_DATA_DIR");
        string folder = Path.Combine(Path.GetTempPath(), "LumaDesk-preset-list-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", folder);
            string settings = Path.Combine(folder, "settings.json");
            File.WriteAllText(settings, before);
            check(JsonSerializer.Serialize(CommandService.ListPresets(Storage.Load()), Storage.Json) == json.RootElement.GetRawText(),
                "Offline saved preferences produce the same catalog as the running application's library");
            check(File.ReadAllText(settings) == before && Directory.GetFiles(folder).Length == 1,
                "Reading the saved preset catalog does not rewrite preferences or create other files");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", originalFolder);
            Directory.Delete(folder, true);
        }

        preferences.Presets[0].Name = "Renamed";
        preferences.Presets[0].Displays.Clear();
        preferences.Presets.Clear();
        check(JsonSerializer.Serialize(reply, Storage.Json) == json.RootElement.GetRawText(),
            "Preset replies are snapshots independent of subsequent library edits");
    }
}
