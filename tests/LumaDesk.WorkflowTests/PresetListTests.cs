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
        check(CommandService.ResolvePresetId(preferences, "work-id") == null,
            "An ID assigned to an unsaved preset is not available");
        preferences = JsonSerializer.Deserialize<Preferences>(before, Storage.Json)!;
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
        check(CommandService.Parse(new[] { "--apply-preset-id", entries[0].GetProperty("Id").GetString()!, "--output", "apply.json" }).Command ==
            new ControlCommand("preset-id", "work-id"), "Discovered IDs can be passed to the strict selector");

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

            const string legacy = """
                {"Presets":[{"Name":"Missing"},{"Id":null,"Name":"Null"},{"Id":"","Name":"Empty"},
                {"Id":" ","Name":"Blank"},{"Id":"duplicate","Name":"Duplicate 1"},{"Id":"duplicate","Name":"Duplicate 2"},
                {"Id":"legacy-other","Name":"work-id"},{"Id":"work-id","Name":"Actual"},{"Id":"--output","Name":"Flag"},
                {"Id":"\u0000","Name":"NUL"}]}
                """;
            File.WriteAllText(settings, legacy);
            var first = Storage.Load();
            var second = Storage.Load();
            string catalog = JsonSerializer.Serialize(CommandService.ListPresets(first), Storage.Json);
            using var legacyDoc = JsonDocument.Parse(catalog);
            var ids = legacyDoc.RootElement.GetProperty("Data").EnumerateArray()
                .Select(entry => entry.GetProperty("Id").GetString()).ToArray();
            check(ids.SequenceEqual(new string?[] { null, null, null, null, null, null, "legacy-other", "work-id", null, null }),
                "Legacy records and duplicate or unusable IDs stay visible without emitting unsafe selectors");
            check(catalog == JsonSerializer.Serialize(CommandService.ListPresets(second), Storage.Json) &&
                File.ReadAllText(settings) == legacy && Directory.GetFiles(folder).Length == 1,
                "Two cold catalogs are stable without writing preferences");
            check(ReferenceEquals(CommandService.ResolvePresetId(first, "work-id"), first.Presets[7]) &&
                CommandService.ResolvePresetId(first, "WORK-ID") == null &&
                CommandService.ResolvePresetId(first, "duplicate") == null &&
                CommandService.ResolvePresetId(first, "Missing") == null,
                "Strict selection is unique, ordinal, and never falls back to names");
            check(CommandService.Parse(new[] { "--apply-preset", "work-id", "--output", "apply.json" }).Command ==
                new ControlCommand("preset", "work-id"), "Legacy selector retains its action");

            string generated = first.Presets[0].Id;
            check(CommandService.ResolvePresetId(first, generated) == null, "An ephemeral ID does not resolve");
            Storage.Save(first);
            check(ReferenceEquals(CommandService.ResolvePresetId(first, generated), first.Presets[0]) &&
                CommandService.ResolvePresetId(Storage.Load(), generated)?.Name == "Missing",
                "Successful ordinary save makes the generated ID stable in memory and on disk");
            first.Presets[0].Name = "Renamed";
            Storage.Save(first);
            check(CommandService.ResolvePresetId(Storage.Load(), generated)?.Name == "Renamed",
                "Renaming keeps the persisted ID");
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
