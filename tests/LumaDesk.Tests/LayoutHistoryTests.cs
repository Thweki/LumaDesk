// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class LayoutHistoryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var original = new[] { new ScreenPlacement("A", -1920, 0, 1920, 1080, false), new ScreenPlacement("B", 0, 0, 2560, 1440, true) };
        var history = new LayoutHistory();
        history.Undo(); history.Redo();
        check(history.Current.Count == 0 && !history.CanUndo && !history.CanRedo, "Empty layout history safely ignores undo and redo");
        history.Reset(original);
        check(history.Current.SequenceEqual(original) && !history.CanUndo && !history.CanRedo, "Loaded layout starts with no preview history");
        var moved = original.Select(p => p.Id == "A" ? p with { X = -1800, Y = 15 } : p).ToArray();
        history.Edit(moved);
        check(history.Current.SequenceEqual(moved) && history.CanUndo && !history.CanRedo, "Coordinate or drag edit records the complete preview, even before layout validation");
        var primary = moved.Select(p => p with { Primary = p.Id == "A" }).ToArray();
        history.Edit(primary);
        history.Undo();
        check(history.Current.SequenceEqual(moved) && history.CanUndo && history.CanRedo, "Undo restores primary flags and coordinates together");
        history.Undo(); history.Undo();
        check(history.Current.SequenceEqual(original) && !history.CanUndo && history.CanRedo, "Multiple undo stops at the loaded layout");
        history.Redo(); history.Redo(); history.Redo();
        check(history.Current.SequenceEqual(primary) && history.CanUndo && !history.CanRedo, "Multiple redo restores edits in their original order");
        history.Edit(primary.ToArray()); history.Undo();
        check(history.Current.SequenceEqual(moved), "Identical previews do not create extra undo steps");
        history.Edit(moved.ToArray());
        check(history.CanRedo, "No-op edit preserves the redo path");
        var arranged = moved.Select((p, i) => p with { X = 0, Y = i * 1080 }).ToArray();
        history.Edit(arranged); history.Redo();
        check(history.Current.SequenceEqual(arranged) && !history.CanRedo, "A new arrangement after undo replaces the redo path");
        history.Undo();
        check(history.Current.SequenceEqual(moved), "A multi-screen arrangement is undone as one edit");

        var input = original.ToList();
        history.Reset(input); input.Clear();
        check(history.Current.SequenceEqual(original), "Reload snapshots are isolated from changes to the caller's list");
        input.AddRange(moved); history.Edit(input); input.Clear();
        check(history.Current.SequenceEqual(moved), "Edit snapshots are isolated from changes to the caller's list");
        var snapshot = history.Current;
        history.Undo();
        check(snapshot.SequenceEqual(moved), "Undo does not mutate a previously captured preview");
        var reconnected = new[] { new ScreenPlacement("C", 0, 0, 1920, 1080, true) };
        history.Reset(reconnected); history.Undo(); history.Redo();
        check(history.Current.SequenceEqual(reconnected) && !history.CanUndo && !history.CanRedo, "Reload after apply or topology change clears both stacks and stale display IDs");
    }
}
