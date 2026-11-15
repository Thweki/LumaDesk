// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneLastAppliedTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var preferences = JsonSerializer.Deserialize<Preferences>("{\"Presets\":[]}")!;
        check(preferences.LastPresetApplication == null, "Legacy settings have no invented application history");
        var backend = new FakeBackend();
        var scene = await SceneService.Capture(backend, "Work");
        preferences.Presets.Add(scene);
        var started = DateTimeOffset.UtcNow;
        var result = await SceneService.ApplyAndRemember(backend, preferences, scene);
        var last = preferences.LastPresetApplication!;
        check(result.Applied == 2 && result.Issues.Count == 0 && last != null &&
            last.PresetId == scene.Id && last.PresetName == "Work" && last.AppliedAtUtc >= started &&
            last.AppliedAtUtc <= DateTimeOffset.UtcNow && last.AppliedAtUtc.Offset == TimeSpan.Zero,
            "Successful application records preset identity, name and UTC completion time");
        Storage.Save(preferences);
        preferences = Storage.Load();
        check(JsonSerializer.Serialize(preferences.LastPresetApplication) == JsonSerializer.Serialize(last),
            "Last successful application survives storage and reload");
        scene = preferences.Presets.Single();
        last = preferences.LastPresetApplication!;

        backend.FailBrightness.Add("A");
        result = await SceneService.ApplyAndRemember(backend, preferences, scene);
        check(result.Applied == 1 && result.Issues.Count > 0 && ReferenceEquals(last, preferences.LastPresetApplication),
            "Partial application preserves the previous successful record");
        backend.FailBrightness.Add("B");
        result = await SceneService.ApplyAndRemember(backend, preferences, scene);
        check(result.Applied == 0 && ReferenceEquals(last, preferences.LastPresetApplication),
            "A completely failed application preserves history");
        backend = new FakeBackend(); backend.Screens.RemoveAt(1);
        result = await SceneService.ApplyAndRemember(backend, preferences, scene);
        check(result.Issues.Count > 0 && ReferenceEquals(last, preferences.LastPresetApplication),
            "Disconnected screens cannot produce a successful application record");
        backend = new FakeBackend(); scene.Displays[1].X = 100;
        result = await SceneService.ApplyAndRemember(backend, preferences, scene);
        check(result.Applied == 2 && result.Issues.Count > 0 && ReferenceEquals(last, preferences.LastPresetApplication),
            "A skipped invalid layout does not count as complete success even when all displays apply");
        scene.Displays[1].X = 1920;
        await SceneService.ApplyAndRemember(backend, preferences, scene, undo: true);
        check(ReferenceEquals(last, preferences.LastPresetApplication), "Undo does not rewrite preset application history");
        var empty = new Preset { Name = "Empty" }; preferences.Presets.Add(empty);
        await SceneService.ApplyAndRemember(backend, preferences, empty);
        check(ReferenceEquals(last, preferences.LastPresetApplication), "An empty preset does not claim a successful application");
        var copy = SceneService.Duplicate(scene, "Copy", preferences.Presets);
        await SceneService.ApplyAndRemember(backend, preferences, copy);
        check(ReferenceEquals(last, preferences.LastPresetApplication), "An unsaved snapshot does not replace saved preset history");
        preferences.Presets.Add(copy);
        await SceneService.ApplyAndRemember(backend, preferences, copy);
        check(preferences.LastPresetApplication?.PresetId == copy.Id && preferences.LastPresetApplication.PresetName == "Copy",
            "The next successful saved preset replaces the previous record");
        last = preferences.LastPresetApplication!;
        last.AppliedAtUtc = DateTimeOffset.UnixEpoch;
        await SceneService.ApplyAndRemember(backend, preferences, copy);
        last = preferences.LastPresetApplication!;
        check(last.AppliedAtUtc >= started, "Reapplying the same preset refreshes its application time");
        copy.Name = "Renamed"; preferences.Presets.Remove(copy);
        Storage.Save(preferences);
        check(Storage.Load().LastPresetApplication?.PresetName == "Copy" && last.PresetId == copy.Id,
            "Renaming or deleting the preset retains the historical name and identity");
    }
}
