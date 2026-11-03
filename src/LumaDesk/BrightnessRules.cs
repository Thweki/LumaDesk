// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;

namespace LumaDesk;

public readonly record struct CombinedLevel(int? Hardware, int Dimming);
public static class BrightnessRules
{
    // Zero is the darkest supported level (85% attenuation), not power-off.
    public const int Transition = 20;
    public static CombinedLevel Split(int value, bool hasBacklight)
    {
        value = Math.Clamp(value, 0, 100);
        if (!hasBacklight) return new(null, (int)Math.Round((100 - value) * 0.85));
        return value < Transition
            ? new(0, (int)Math.Round((Transition - value) * 85.0 / Transition))
            : new((int)Math.Round((value - Transition) * 100.0 / (100 - Transition)), 0);
    }
    public static int Combine(int? hardware, int dimming)
    {
        dimming = Math.Clamp(dimming, 0, 85);
        if (!hardware.HasValue) return (int)Math.Round(100 - dimming * 100.0 / 85);
        double baseValue = Transition + Math.Clamp(hardware.Value, 0, 100) * (100 - Transition) / 100.0;
        return (int)Math.Round(dimming > 0 ? baseValue * (1 - dimming / 85.0) : baseValue);
    }
}
