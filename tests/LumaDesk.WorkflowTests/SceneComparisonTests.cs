// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneComparisonTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var backend = new FakeBackend();
        var saved = await SceneService.Capture(backend, "Work");
        string original = JsonSerializer.Serialize(saved);
        backend.Screens[0].Brightness = 80;
        backend.FailColor = true;
        int reads = 0;
        var current = await SceneService.Capture(backend.Displays, d => { reads++; return backend.Color(d); }, "");
        var comparison = SceneComparison.Compare(saved, current);
        check(reads == 2 && backend.Calls.Count == 0,
            "Comparison capture only reads displays and color, without any hardware write calls");
        check(comparison.Differences.Any(d => d.Setting == "scene.brightness" && d.Current == "80%") &&
            comparison.Differences.Any(d => d.Setting == "HDR" && d.Current == L.T("s4d8c1c5b42")),
            "Partial color read failures preserve fresh brightness and report unknown HDR");
        check(JsonSerializer.Serialize(saved) == original,
            "Capturing a comparison preserves the saved preset");
        backend.FailColor = false;
        current = await SceneService.Capture(backend.Displays, backend.Color, "");
        check(!SceneComparison.Compare(saved, current).Differences.Any(d => d.Setting == "HDR") && backend.Calls.Count == 0,
            "A later comparison retries failed reads without applying the scene");
    }
}
