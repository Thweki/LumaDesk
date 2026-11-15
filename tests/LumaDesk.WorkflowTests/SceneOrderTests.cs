// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneOrderTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var first = await SceneService.Capture(new FakeBackend(), "Work");
        first.OnConnect = true; first.AtTime = "09:00"; first.OnPower = "ac";
        first.Shortcut = new() { Modifiers = 3, Key = 0x57 };
        first.Displays[0].Dpi = 150; first.Displays[0].Hdr = true;
        var second = new Preset { Name = "Evening" };
        var third = new Preset { Name = "Travel" };
        var presets = new List<Preset> { first, second, third };
        var originals = presets.ToDictionary(p => p.Id, p => JsonSerializer.Serialize(p, Storage.Json));
        var beforeBackend = new FakeBackend();
        var before = await SceneService.Apply(beforeBackend, first);

        check(SceneService.Move(presets, second, -1) && presets.SequenceEqual(new[] { second, first, third }),
            "Move up swaps only the selected preset and its previous neighbor");
        check(SceneService.Move(presets, first, 1) && presets.SequenceEqual(new[] { second, third, first }),
            "Move down swaps only the selected preset and its next neighbor");
        check(SceneService.Move(presets, first, -1) && SceneService.Move(presets, first, -1) &&
            presets.SequenceEqual(new[] { first, second, third }), "Repeated moves follow the preset's current position");
        check(!SceneService.Move(presets, first, -1) && !SceneService.Move(presets, third, 1) &&
            !SceneService.Move(presets, new Preset { Id = second.Id }, 1) &&
            presets.SequenceEqual(new[] { first, second, third }),
            "Boundaries and stale preset objects cannot reorder or replace saved presets");
        foreach (int direction in new[] { 0, 2, -2, int.MinValue, int.MaxValue })
            check(!SceneService.Move(presets, second, direction) && presets.SequenceEqual(new[] { first, second, third }),
                "Invalid move direction leaves the order untouched: " + direction);
        check(!SceneService.Move(new(), first, 1) && !SceneService.Move(new() { first }, first, -1) &&
            !SceneService.Move(new() { first }, first, 1), "Empty and single-preset lists cannot move");

        SceneService.Move(presets, third, -1);
        SceneService.Move(presets, third, -1);
        check(presets.All(p => JsonSerializer.Serialize(p, Storage.Json) == originals[p.Id]),
            "Reordering preserves every preset ID, name, saved display value, shortcut and automation setting");
        Storage.Save(new Preferences { Presets = presets });
        var restored = Storage.Load().Presets;
        check(restored.Select(p => p.Id).SequenceEqual(new[] { third.Id, first.Id, second.Id }) &&
            restored.All(p => JsonSerializer.Serialize(p, Storage.Json) == originals[p.Id]),
            "Reordered presets and their full contents survive saving and loading settings");
        var afterBackend = new FakeBackend();
        var after = await SceneService.Apply(afterBackend, restored.Single(p => p.Id == first.Id));
        check(beforeBackend.Calls.SequenceEqual(afterBackend.Calls) && before.Applied == after.Applied &&
            before.Issues.SequenceEqual(after.Issues), "Applying a preset by stable ID behaves identically after reordering and reload");
    }
}
