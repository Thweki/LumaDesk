// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneDuplicateTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var source = new Preset
        {
            Name = "Work", OnConnect = true, AtTime = "09:00", OnPower = "ac",
            Shortcut = new() { Modifiers = 3, Key = 0x57 },
            Displays = new()
            {
                new() { Id = "A", Name = "Main", Brightness = 35, Dimming = 0.25,
                    Mode = new(1920, 1080, 60), Orientation = 0, Dpi = 150, Hdr = true,
                    UserProfiles = false, SdrProfile = "", HdrProfile = "HDR.icm", X = 0, Y = 0, Primary = true },
                new() { Id = "B", Name = "Side", Brightness = null, Dimming = 0.5,
                    Mode = new(1920, 1080, 60), Orientation = 0, Dpi = 100, Hdr = false,
                    UserProfiles = true, SdrProfile = "sRGB.icm", HdrProfile = null, X = 1920, Y = 0, Primary = false }
            }
        };
        var presets = new List<Preset> { source };
        string original = JsonSerializer.Serialize(source, Storage.Json);
        var beforeBackend = new FakeBackend();
        var before = await SceneService.Apply(beforeBackend, source);
        var copy = SceneService.Duplicate(source, "  Evening  ", presets);
        presets.Add(copy);
        check(copy.Name == "Evening" && Guid.TryParseExact(copy.Id, "N", out _) && copy.Id != source.Id,
            "Duplicate gets a trimmed name and fresh stable identity");
        check(JsonSerializer.Serialize(copy.Displays, Storage.Json) == JsonSerializer.Serialize(source.Displays, Storage.Json),
            "Duplicate preserves every saved display value, order, null and explicit empty profile");
        check(!ReferenceEquals(copy.Displays, source.Displays) && copy.Displays.Zip(source.Displays).All(pair => !ReferenceEquals(pair.First, pair.Second)),
            "Duplicate owns its display list and every mutable display entry");
        check(!copy.OnConnect && copy.AtTime == null && copy.OnPower == null && copy.Shortcut == null,
            "Duplicate starts without connection, schedule, power or shortcut triggers");
        var second = SceneService.Duplicate(source, "Travel", presets);
        check(second.Id != copy.Id && second.Id != source.Id, "Repeated duplication creates distinct identities");
        copy.Displays[0].Brightness = 99; copy.Displays[0].Mode = new(1280, 720, 75);
        copy.Displays[0].HdrProfile = "changed.icm"; copy.Displays.RemoveAt(1);
        check(JsonSerializer.Serialize(source, Storage.Json) == original,
            "Editing or deleting copied display settings leaves the source and its automation untouched");
        source.Displays[1].Name = "Changed source";
        check(second.Displays[1].Name == "Side", "Source edits do not alter previously duplicated settings");
        source.Displays[1].Name = "Side";
        var afterBackend = new FakeBackend();
        var after = await SceneService.Apply(afterBackend, source);
        check(beforeBackend.Calls.SequenceEqual(afterBackend.Calls) && before.Applied == after.Applied && before.Issues.SequenceEqual(after.Issues),
            "Original preset applies identically after duplication and independent edits");

        var legacy = JsonSerializer.Deserialize<Preset>("{\"Name\":\"Old\",\"Displays\":[{\"Id\":\"A\",\"Brightness\":20,\"Dimming\":5}]}")!;
        var legacyCopy = SceneService.Duplicate(legacy, "Old copy", new[] { legacy });
        check(JsonSerializer.Serialize(legacyCopy.Displays, Storage.Json) == JsonSerializer.Serialize(legacy.Displays, Storage.Json),
            "Legacy brightness-only presets retain unspecified fields when duplicated");
        var legacyBackend = new FakeBackend();
        await SceneService.Apply(legacyBackend, legacyCopy);
        check(legacyBackend.Calls.SequenceEqual(new[] { "brightness:A" }), "Duplicated legacy preset still applies brightness only");
        foreach (var name in new[] { "", " \t ", "work", " Work ", "EVENING", new string('x', 41) })
        {
            bool rejected = false;
            try { SceneService.Duplicate(source, name, presets); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && presets.Count == 2 && JsonSerializer.Serialize(source, Storage.Json) == original,
                "Invalid or conflicting duplicate name is rejected without changing presets: " + name);
        }
        check(SceneService.Duplicate(source, new string('x', 40), presets).Name.Length == 40,
            "Duplicate accepts names at the editor's 40-character limit");
        var restored = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(second, Storage.Json), Storage.Json)!;
        check(restored.Id == second.Id && restored.Name == second.Name &&
            JsonSerializer.Serialize(restored.Displays, Storage.Json) == JsonSerializer.Serialize(second.Displays, Storage.Json) &&
            !restored.OnConnect && restored.Shortcut == null && restored.AtTime == null && restored.OnPower == null,
            "Duplicate identity, saved display values and disabled triggers survive a settings roundtrip");
    }
}
