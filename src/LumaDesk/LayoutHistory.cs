// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Collections.Generic;
using System.Linq;

namespace LumaDesk;

// Preview history only; applying or reloading a layout starts a new history.
internal sealed class LayoutHistory
{
    private readonly Stack<IReadOnlyList<ScreenPlacement>> undo = new(), redo = new();
    internal IReadOnlyList<ScreenPlacement> Current { get; private set; } = new List<ScreenPlacement>().AsReadOnly();
    internal bool CanUndo => undo.Count > 0;
    internal bool CanRedo => redo.Count > 0;

    internal void Reset(IEnumerable<ScreenPlacement> placements)
    {
        Current = placements.ToList().AsReadOnly();
        undo.Clear(); redo.Clear();
    }
    internal void Edit(IEnumerable<ScreenPlacement> placements)
    {
        var next = placements.ToList().AsReadOnly();
        if (Current.SequenceEqual(next)) return;
        undo.Push(Current); Current = next; redo.Clear();
    }
    internal void Undo()
    {
        if (!CanUndo) return;
        redo.Push(Current); Current = undo.Pop();
    }
    internal void Redo()
    {
        if (!CanRedo) return;
        undo.Push(Current); Current = redo.Pop();
    }
}
