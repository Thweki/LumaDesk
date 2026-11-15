// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;

namespace LumaDesk;

// State machine shared by UI and tests. Revert failure leaves recovery armed for retry.
public sealed class ModeLease
{
    private readonly Action restore;
    private readonly Action? disarmRecovery;
    public bool Pending { get; private set; }
    public ModeLease(Action apply, Action restore, Action? disarmRecovery = null)
    {
        this.restore = restore;
        this.disarmRecovery = disarmRecovery;
        Pending = true;
        try { apply(); }
        catch { Revert(); throw; }
    }
    public void Keep() { if (!Pending) return; disarmRecovery?.Invoke(); Pending = false; }
    // This also disarms recovery when an apply failure rolls back inside the constructor.
    public void Revert() { if (!Pending) return; restore(); Keep(); }
}
