// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Collections.Generic;

namespace LumaDesk;

// A session cursor, not a claim that the hardware still matches a saved preset.
internal sealed class SceneCycle
{
    private string? lastAppliedId;

    internal Preset? Select(IReadOnlyList<Preset> presets, int direction)
    {
        if (presets.Count == 0 || direction is not (-1 or 1)) return null;
        int index = -1;
        for (int i = 0; i < presets.Count; i++)
            if (presets[i].Id == lastAppliedId) { index = i; break; }
        if (index < 0) return direction == 1 ? presets[0] : presets[presets.Count - 1];
        return presets[(index + direction + presets.Count) % presets.Count];
    }

    internal void RecordApplied(Preset preset, SceneResult result, bool undo = false)
    {
        if (result.Issues.Count != 0 || result.Applied == 0) return;
        lastAppliedId = undo ? null : preset.Id;
    }
}
