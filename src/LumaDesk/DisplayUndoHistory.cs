// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Threading.Tasks;

namespace LumaDesk;

// One session-local slot shared by preset switches and manual output changes.
// Recording happens only after a manual change has passed hardware readback.
internal sealed class DisplayUndoHistory
{
    internal Preset? Snapshot { get; private set; }
    internal bool OutputOnly { get; private set; }

    internal void Remember(Preset snapshot, bool outputOnly = false)
    {
        Snapshot = snapshot;
        OutputOnly = outputOnly;
    }

    internal async Task<SceneResult?> UndoAsync(ISceneBackend backend)
    {
        if (Snapshot == null) return null;
        var result = await SceneService.Apply(backend, Snapshot, OutputOnly);
        // Missing displays and failed operations retain the original target for retry.
        if (result.Issues.Count == 0) Snapshot = null;
        return result;
    }
}
