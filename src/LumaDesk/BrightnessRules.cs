// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;

namespace LumaDesk;

public readonly record struct CombinedLevel(int? Hardware, double Dimming);
public static class BrightnessRules
{
    // Zero is the darkest supported level (85% attenuation), not power-off.
    public const int Transition = 20;
    public const int MaxDimming = 85;

    public static CombinedLevel Split(int value, bool hasBacklight)
    {
        value = Math.Clamp(value, 0, 100);
        if (!hasBacklight) return new(null, (100 - value) * MaxDimming / 100.0);
        return value < Transition
            ? new(0, (Transition - value) * (double)MaxDimming / Transition)
            : new((int)Math.Round((value - Transition) * 100.0 / (100 - Transition)), 0);
    }

    // The overlay attenuates both the minimum backlight and the light above it.
    // At maximum dimming, 15% of that additional light is still visible.
    private static double Level(int hardware, double dimming) =>
        Transition * (1 - dimming / (double)MaxDimming) +
        (100 - Transition) * hardware / 100.0 * (1 - dimming / 100.0);

    public static int Combine(int? hardware, double dimming)
    {
        dimming = Math.Clamp(dimming, 0, MaxDimming);
        if (!hardware.HasValue) return (int)Math.Round(100 - dimming * 100.0 / MaxDimming);
        return (int)Math.Round(Level(Math.Clamp(hardware.Value, 0, 100), dimming));
    }

    public static CombinedLevel Adjust(int value, int? hardware, double dimming)
    {
        value = Math.Clamp(value, 0, 100);
        hardware = hardware.HasValue ? Math.Clamp(hardware.Value, 0, 100) : null;
        dimming = Math.Clamp(dimming, 0, MaxDimming);
        int current = Combine(hardware, dimming);
        if (value is 0 or 100) return Split(value, hardware.HasValue);
        if (value == current) return new(hardware, dimming);

        // A preset or a monitor button can leave both components active. Move
        // each in the requested direction, including while crossing into the normal range.
        if (hardware is > 0 && dimming > 0)
        {
            double unshaded = Level(hardware.Value, 0);
            if (value > current && value < unshaded)
            {
                double fraction = (unshaded - value) / (unshaded - Level(hardware.Value, MaxDimming));
                return new(hardware, Math.Round(fraction * MaxDimming, 2));
            }
            double minimumBacklight = Level(0, dimming);
            if (value < current && value > minimumBacklight)
            {
                double fraction = (value - minimumBacklight) / (Level(100, dimming) - minimumBacklight);
                return new((int)Math.Round(fraction * 100), dimming);
            }
        }
        return Split(value, hardware.HasValue);
    }
}
