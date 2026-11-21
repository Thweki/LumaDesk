// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneContrastTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        Preset Target(params (string Id, int? Contrast)[] values) => new()
        {
            Displays = values.Select(v => new PresetDisplay { Id = v.Id, Contrast = v.Contrast }).ToList()
        };
        FakeBackend Supported()
        {
            var result = new FakeBackend();
            result.Screens[0].Contrast = 50;
            result.Screens[1].Contrast = 60;
            return result;
        }

        var backend = Supported();
        backend.Screens[0].Contrast = 0;
        backend.Screens[1].Contrast = null;
        var captured = await SceneService.Capture(backend, "Reading");
        check(captured.Displays[0].Contrast == 0 && captured.Displays[1].Contrast == null && backend.Calls.Count == 0,
            "Capture distinguishes zero contrast from unavailable contrast without writes");
        var preferences = new Preferences { Presets = new() { captured } };
        Storage.Save(preferences);
        var restored = Storage.Load().Presets.Single();
        check(restored.Displays[0].Contrast == 0 && restored.Displays[1].Contrast == null,
            "Saved contrast and unspecified contrast survive settings persistence");
        var copy = SceneService.Duplicate(restored, "Reading copy", preferences.Presets);
        check(copy.Displays[0].Contrast == 0 && copy.Displays[1].Contrast == null,
            "Duplicate retains saved contrast including zero and unspecified values");
        copy.Displays[0].Contrast = 100;
        check(restored.Displays[0].Contrast == 0, "Editing copied contrast leaves the original preset unchanged");

        backend = Supported();
        var legacy = JsonSerializer.Deserialize<Preset>("{\"Displays\":[{\"Id\":\"A\",\"Brightness\":20}]}")!;
        var result = await SceneService.Apply(backend, legacy);
        check(legacy.Displays[0].Contrast == null && result.Issues.Count == 0 && backend.Screens[0].Contrast == 50 &&
            backend.Calls.SequenceEqual(new[] { "brightness:A" }), "Legacy presets never write contrast on supported displays");
        backend.Calls.Clear();
        await SceneService.Apply(backend, Target(("A", null)));
        check(backend.Calls.SequenceEqual(new[] { "brightness:A" }), "Explicit null contrast leaves the monitor unchanged");

        foreach (int percent in new[] { 0, 67, 100 })
        {
            backend = Supported();
            result = await SceneService.Apply(backend, Target(("a", percent), ("B", 80)));
            check(result.Applied == 2 && result.Issues.Count == 0 && backend.Screens[0].Contrast == percent && backend.Screens[1].Contrast == 80,
                "Each display receives its own saved contrast using case-insensitive identity: " + percent);
        }
        backend.Calls.Clear();
        await SceneService.Apply(backend, Target(("A", 100), ("B", 80)));
        check(!backend.Calls.Any(c => c.StartsWith("contrast:")), "Matching contrast avoids redundant DDC writes");
        backend = Supported();
        backend.Screens.Reverse();
        await SceneService.Apply(backend, Target(("A", 20)));
        check(backend.Screens.Single(d => d.Id == "A").Contrast == 20 && backend.Screens.Single(d => d.Id == "B").Contrast == 60,
            "Reordered monitors are targeted by saved identity, leaving other contrast values alone");

        foreach (int invalid in new[] { -1, 101, int.MinValue, int.MaxValue })
        {
            backend = Supported();
            result = await SceneService.Apply(backend, Target(("A", invalid), ("B", 80)));
            check(result.Applied == 1 && result.Issues.Count == 1 && result.Issues[0].Contains("Contrast") &&
                !backend.Calls.Contains("contrast:A") && backend.Calls.Contains("brightness:A") && backend.Screens[1].Contrast == 80,
                "Invalid saved contrast is rejected before a write while other operations continue: " + invalid);
        }
        backend = Supported();
        backend.Screens[0].Contrast = null;
        result = await SceneService.Apply(backend, Target(("A", 25), ("B", 75)));
        check(result.Applied == 1 && result.Issues.Single().Contains(L.T("scene.unsupported")) &&
            !backend.Calls.Contains("contrast:A") && backend.Calls.Contains("brightness:A") && backend.Screens[1].Contrast == 75,
            "Unsupported contrast reports partial completion without blocking brightness or other monitors");
        backend = Supported();
        backend.FailContrast.Add("A");
        result = await SceneService.Apply(backend, Target(("A", 25), ("B", 75)));
        check(result.Applied == 1 && result.Issues.Single().Contains("A · Contrast") && backend.Screens[0].Contrast == 50 &&
            backend.Calls.Contains("brightness:A") && backend.Screens[1].Contrast == 75,
            "Failed contrast writes retain the previous state and report the failing display and control");

        backend = Supported();
        var modeScene = Target(("A", 25));
        modeScene.Displays[0].Mode = new(1280, 720, 60);
        var oldDisplay = backend.Screens[0];
        result = await SceneService.Apply(backend, modeScene);
        check(result.Issues.Count == 0 && backend.StaleReferences == 0 && !ReferenceEquals(oldDisplay, backend.Screens[0]) &&
            oldDisplay.Contrast == 50 && backend.Screens[0].Contrast == 25 && backend.Calls.IndexOf("mode:A") < backend.Calls.IndexOf("contrast:A"),
            "Contrast uses the newly enumerated monitor after an output-mode change");
        backend = Supported();
        var hdrScene = Target(("A", 25));
        hdrScene.Displays[0].Hdr = true;
        backend.AfterHdr = d => backend.Screens[backend.Screens.IndexOf(d)] = new DisplayInfo { Id = d.Id, Name = d.Name };
        result = await SceneService.Apply(backend, hdrScene);
        check(result.Applied == 0 && result.Issues.Single().Contains(L.T("scene.unsupported")) &&
            !backend.Calls.Contains("contrast:A") && backend.Calls.Contains("brightness:A") && backend.StaleReferences == 0,
            "HDR refresh can remove contrast support without a stale or unsupported contrast write");

        backend = Supported();
        result = await SceneService.Apply(backend, Target(("missing", 20), ("A", 30), ("a", 40)));
        check(result.Applied == 1 && result.Issues.Single().Contains(L.T("scene.disconnected")) &&
            backend.Calls.Count(c => c.StartsWith("contrast:")) == 1 && backend.Screens[0].Contrast == 30,
            "Disconnected and duplicate identities never send extra contrast writes");
        backend = Supported();
        var before = await SceneService.Capture(backend, "Before");
        await SceneService.Apply(backend, Target(("A", 10), ("B", 90)));
        result = await SceneService.Apply(backend, before);
        check(result.Applied == 2 && result.Issues.Count == 0 && backend.Screens[0].Contrast == 50 && backend.Screens[1].Contrast == 60,
            "The existing pre-apply snapshot restores each monitor's previous contrast");
        backend = Supported();
        backend.FailColor = true;
        captured = await SceneService.Capture(backend, "No color API");
        check(captured.Displays[0].Contrast == 50 && captured.Displays[1].Contrast == 60,
            "Unavailable HDR/ICC queries do not discard readable DDC contrast");
    }
}
