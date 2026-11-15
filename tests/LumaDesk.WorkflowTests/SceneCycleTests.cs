// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneCycleTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var first = await SceneService.Capture(new FakeBackend(), "Work");
        var second = await SceneService.Capture(new FakeBackend(), "Evening");
        var third = await SceneService.Capture(new FakeBackend(), "Travel");
        var presets = new List<Preset> { first, second, third };
        var cycle = new SceneCycle();
        var original = JsonSerializer.Serialize(presets, Storage.Json);
        check(cycle.Select(presets, 1) == first && cycle.Select(presets, -1) == third,
            "Without a successful apply, next starts at the first preset and previous at the last");
        check(cycle.Select(new List<Preset>(), 1) == null && cycle.Select(new List<Preset>(), -1) == null,
            "Empty preset lists have no cycle target");
        foreach (int direction in new[] { 0, 2, -2, int.MinValue, int.MaxValue })
            check(cycle.Select(presets, direction) == null, "Invalid cycle direction has no target: " + direction);

        async Task Apply(Preset preset, FakeBackend? backend = null, bool undo = false)
        {
            var result = await SceneService.Apply(backend ?? new FakeBackend(), preset);
            cycle.RecordApplied(preset, result, undo);
        }
        await Apply(first);
        check(cycle.Select(presets, 1) == second && cycle.Select(presets, -1) == third,
            "A successful direct apply anchors both directions, including backward wraparound");
        await Apply(second);
        check(cycle.Select(presets, 1) == third && cycle.Select(presets, -1) == first,
            "Cycling follows saved order in both directions");
        await Apply(third);
        check(cycle.Select(presets, 1) == first && cycle.Select(presets, -1) == second,
            "Next wraps from the last preset to the first");
        check(JsonSerializer.Serialize(presets, Storage.Json) == original,
            "Selecting and recording cycle targets does not mutate presets or automation");

        var reloaded = JsonSerializer.Deserialize<List<Preset>>(original, Storage.Json)!;
        reloaded[2].Name = "Renamed";
        SceneService.Move(reloaded, reloaded[2], -1);
        check(cycle.Select(reloaded, 1)?.Id == second.Id && cycle.Select(reloaded, -1)?.Id == first.Id,
            "The cursor follows stable IDs after object replacement, renaming and reordering");
        presets.Remove(third);
        check(cycle.Select(presets, 1) == first && cycle.Select(presets, -1) == second,
            "Deleting the anchor resets selection to the appropriate end of the current list");
        presets = new() { first };
        await Apply(first);
        check(cycle.Select(presets, 1) == first && cycle.Select(presets, -1) == first,
            "A single preset can be reapplied in either direction");

        presets = new() { first, second, third };
        var partial = new FakeBackend(); partial.FailBrightness.Add("B");
        await Apply(second, partial);
        check(cycle.Select(presets, 1) == second, "Partial application does not advance the cursor");
        var failed = new FakeBackend(); failed.FailBrightness.UnionWith(new[] { "A", "B" });
        await Apply(third, failed);
        check(cycle.Select(presets, 1) == second, "Fully failed application does not advance the cursor");
        await Apply(new Preset());
        check(cycle.Select(presets, 1) == second, "An empty preset cannot become a successful anchor");
        await Apply(third, partial, undo: true);
        check(cycle.Select(presets, 1) == second, "A failed undo preserves the cursor");
        await Apply(third, undo: true);
        check(cycle.Select(presets, 1) == first && cycle.Select(presets, -1) == third,
            "Successful undo clears the cursor because its snapshot is not a saved preset");
        await Apply(second);
        check(new SceneCycle().Select(presets, 1) == first, "A new session starts without a persisted cursor");
    }
}
