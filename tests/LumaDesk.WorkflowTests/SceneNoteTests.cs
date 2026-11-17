// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneNoteTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var legacy = JsonSerializer.Deserialize<Preset>("{\"Name\":\"Old\",\"Displays\":[]}", Storage.Json)!;
        check(legacy.Note == null && new Preset().Note == null, "Legacy and new presets start without a note");
        var scene = new Preset
        {
            Name = "Work", OnConnect = true, AtTime = "09:00", OnPower = "ac", Shortcut = new() { Key = 0x57 },
            Displays = new() { new() { Id = "A", Brightness = 25, Dimming = 0.2 } }
        };
        string original = JsonSerializer.Serialize(scene, Storage.Json);
        var beforeBackend = new FakeBackend();
        var before = await SceneService.Apply(beforeBackend, scene);
        const string memo = "Evening reading\n阅读 · 読書 · 독서 🌙";
        SceneService.SetNote(scene, "  " + memo + " \t");
        check(scene.Note == memo, "Notes trim outer whitespace and preserve multiline Unicode text");
        var restored = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences { Presets = new() { scene } }, Storage.Json), Storage.Json)!;
        check(restored.Presets.Single().Note == memo && restored.Presets[0].Id == scene.Id,
            "Note and preset identity survive the settings roundtrip");
        var copy = SceneService.Duplicate(scene, "Evening", new[] { scene });
        check(copy.Note == memo, "Duplicating a preset includes its saved note");
        SceneService.SetNote(copy, "Different use");
        check(scene.Note == memo, "Editing a duplicate's note leaves the source untouched");
        var afterBackend = new FakeBackend();
        var after = await SceneService.Apply(afterBackend, scene);
        check(beforeBackend.Calls.SequenceEqual(afterBackend.Calls) && before.Applied == after.Applied && before.Issues.SequenceEqual(after.Issues),
            "Notes do not change preset application or hardware operations");
        SceneService.SetNote(scene, new string('x', 500));
        check(scene.Note?.Length == 500, "Notes accept the 500-character boundary");
        bool rejected = false;
        try { SceneService.SetNote(scene, new string('x', 501)); }
        catch (ArgumentException) { rejected = true; }
        check(rejected && scene.Note?.Length == 500, "Oversized notes are rejected without changing saved text");
        foreach (string? empty in new string?[] { "", " \r\n\t ", null })
        {
            SceneService.SetNote(scene, empty);
            check(scene.Note == null && JsonSerializer.Serialize(scene, Storage.Json) == original,
                "Clearing a note preserves every other preset field");
        }
    }
}
