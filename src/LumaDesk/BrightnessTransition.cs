// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDesk;

// Called inside the display's brightness gate. Never runs hardware writes in parallel.
internal static class BrightnessTransition
{
    internal const string EnvironmentVariable = "LUMADESK_EXPERIMENTAL_TRANSITIONS";
    internal static bool IsEnabled(string? value) => value == "1";

    internal static async Task ApplyAsync(CombinedLevel start, CombinedLevel target, bool enabled,
        Func<CombinedLevel, Task> write, CancellationToken cancellation = default,
        Func<TimeSpan>? elapsed = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!enabled || start == target)
        {
            await write(target);
            return;
        }

        var clock = Stopwatch.StartNew();
        elapsed ??= () => clock.Elapsed;
        delay ??= (duration, token) => Task.Delay(duration, token);
        var previous = start;
        // Four samples at most; slow DDC calls consume the same 240 ms budget.
        // Do not replay missed frames or add a full delay after a slow write.
        for (int sample = 1; sample <= 4; sample++)
        {
            var remaining = TimeSpan.FromMilliseconds(sample * 60) - elapsed();
            if (remaining > TimeSpan.Zero) await delay(remaining, cancellation);
            cancellation.ThrowIfCancellationRequested();
            double progress = sample == 4 ? 1 : Math.Clamp(elapsed().TotalMilliseconds / 240, 0, 1);
            double eased = progress * progress * (3 - 2 * progress);
            var level = progress >= 1 ? target : new CombinedLevel(
                start.Hardware.HasValue && target.Hardware.HasValue
                    ? (int)Math.Round(start.Hardware.Value + (target.Hardware.Value - start.Hardware.Value) * eased)
                    : target.Hardware,
                start.Dimming + (target.Dimming - start.Dimming) * eased);
            if (level != previous)
            {
                await write(level);
                previous = level;
            }
            if (progress >= 1) break;
        }
    }
}
