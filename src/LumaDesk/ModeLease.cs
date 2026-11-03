// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;

namespace LumaDesk;

// State machine shared by UI and tests. Revert failure leaves the lease pending for retry.
public sealed class ModeLease
{
    private readonly Action restore;
    public bool Pending { get; private set; }
    public ModeLease(Action apply, Action restore)
    {
        this.restore = restore;
        Pending = true;
        try { apply(); }
        catch { Revert(); throw; }
    }
    public void Keep() => Pending = false;
    public void Revert() { if (!Pending) return; restore(); Pending = false; }
}
