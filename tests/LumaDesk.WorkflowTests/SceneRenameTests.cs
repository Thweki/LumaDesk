// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.Text.Json;

internal static class SceneRenameTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var scene = new Preset
        {
            Name = "Work", OnConnect = true, AtTime = "21:30", OnPower = "ac",
            Shortcut = new() { Modifiers = 3, Key = 0x57 },
            Displays = new()
            {
                new() { Id = "A", Name = "Desk", Brightness = 30, Dimming = 12.5,
                    Mode = new(1280, 720, 60), Orientation = 0, Dpi = 150, Hdr = true,
                    UserProfiles = true, SdrProfile = "work.icm", HdrProfile = "hdr.icm",
                    X = 0, Y = 0, Primary = true },
                new() { Id = "B", Name = "Side", Brightness = null, Dimming = 20,
                    Mode = new(1920, 1080, 60), Orientation = 0, X = 1280, Y = 0, Primary = false }
            }
        };
        var other = new Preset { Name = "Night" };
        var presets = new List<Preset> { other, scene };
        string Encode(object value) => JsonSerializer.Serialize(value, Storage.Json);
        string otherBefore = Encode(other);
        var expected = JsonSerializer.Deserialize<Preset>(Encode(scene), Storage.Json)!;
        expected.Name = "办公";
        var displays = scene.Displays;
        var shortcut = scene.Shortcut;
        var beforeBackend = new FakeBackend();
        var beforeResult = await SceneService.Apply(beforeBackend, scene);

        SceneService.Rename(presets, scene, "  办公  ");
        check(Encode(scene) == Encode(expected), "Rename trims Unicode names and changes only Name, preserving every saved field");
        check(ReferenceEquals(presets[1], scene) && ReferenceEquals(scene.Displays, displays) && ReferenceEquals(scene.Shortcut, shortcut),
            "Rename preserves preset, display list and shortcut objects for existing callbacks");
        check(presets.Count == 2 && ReferenceEquals(presets[0], other) && Encode(other) == otherBefore,
            "Rename preserves preset ordering and other presets");
        var afterBackend = new FakeBackend();
        var afterResult = await SceneService.Apply(afterBackend, scene);
        check(beforeBackend.Calls.SequenceEqual(afterBackend.Calls) && beforeResult.Applied == afterResult.Applied &&
            beforeResult.Issues.SequenceEqual(afterResult.Issues), "Renamed preset applies the same operations with the same result");

        foreach (string invalid in new[] { "", " \t\r\n", "Night", " nIgHt ", new string('x', 41) })
        {
            string before = Encode(presets);
            bool rejected = false;
            try { SceneService.Rename(presets, scene, invalid); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && Encode(presets) == before, "Invalid rename leaves all presets unchanged: " + JsonSerializer.Serialize(invalid));
        }
        SceneService.Rename(presets, scene, "Work");
        SceneService.Rename(presets, scene, "work");
        check(scene.Name == "work", "Case-only rename does not collide with itself");
        string unchanged = Encode(presets);
        SceneService.Rename(presets, scene, " work ");
        check(Encode(presets) == unchanged, "Renaming to the same trimmed name is harmless");
        SceneService.Rename(presets, scene, new string('x', 40));
        check(scene.Name.Length == 40, "Rename accepts the full 40-character name limit");

        var detached = new Preset { Id = scene.Id, Name = "Detached" };
        bool missing = false;
        try { SceneService.Rename(presets, detached, "New name"); }
        catch (InvalidOperationException) { missing = true; }
        check(missing && detached.Name == "Detached" && scene.Name.Length == 40,
            "Rename rejects a preset that is not in the saved collection, even with a matching ID");
        var legacy = JsonSerializer.Deserialize<Preferences>("{\"Presets\":[{\"Name\":\"Old\",\"Displays\":[]}]}")!;
        string legacyId = legacy.Presets[0].Id;
        SceneService.Rename(legacy.Presets, legacy.Presets[0], "New");
        check(legacy.Presets[0].Name == "New" && legacy.Presets[0].Id == legacyId && legacy.Presets[0].Displays.Count == 0,
            "Legacy and empty presets can be renamed without capturing connected screens");
    }
}
