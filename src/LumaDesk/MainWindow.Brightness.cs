// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDesk;

public partial class MainWindow
{
    private readonly Dictionary<string, SemaphoreSlim> brightnessGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool brightnessTransitionsEnabled = BrightnessTransition.IsEnabled(
        Environment.GetEnvironmentVariable(BrightnessTransition.EnvironmentVariable));
    private readonly CancellationTokenSource brightnessTransitionLifetime = new();
    internal event Action<DisplayInfo>? BrightnessChanged;
    internal int GetCombinedBrightness(DisplayInfo display)
    {
        var current = Displays.FirstOrDefault(d => d.Id == display.Id) ?? display;
        return BrightnessRules.Combine(current.Brightness, current.Dimming);
    }
    internal async Task ChangeCombinedBrightness(DisplayInfo display, int value)
    {
        var source = CurrentBrightnessDisplay(display.Id);
        var targets = BrightnessTargets(source).Select(target => new BrightnessTarget(target,
            GroupRules.Target(value, Options(source), Options(target)))).ToArray();
        await ApplyCombinedBrightness(targets);
    }

    // Explicit selection deliberately bypasses global sync, group membership and
    // relative offsets. Device-specific min/max boundaries still apply.
    internal IReadOnlyList<BrightnessTarget> SelectedBrightnessTargets(IEnumerable<string> ids, int value)
    {
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Displays.Where(d => selected.Contains(d.Id)).DistinctBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => new BrightnessTarget(d, GroupRules.Target(value, Options(d), Options(d)))).ToArray();
    }

    internal async Task ChangeSelectedBrightness(IReadOnlyList<string> ids, int value)
    {
        // Resolve fresh objects for every queued value, never reuse an enumeration
        // snapshot or silently substitute a new screen after a hotplug.
        var targets = SelectedBrightnessTargets(ids, value);
        if (targets.Count == 0) throw new InvalidOperationException(L.T("quick.multiDisconnected"));
        await ApplyCombinedBrightness(targets);
        if (targets.Count < ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidOperationException(L.T("quick.multiDisconnected"));
    }

    private async Task ApplyCombinedBrightness(IReadOnlyList<BrightnessTarget> targets)
    {
        if (sceneBusy) throw new InvalidOperationException(L.T("s92f637408f"));
        foreach (var request in targets) CancelReconnect(request.Display.Id);
        while (brightnessScan is { } scan) await scan.Task;
        if (sceneBusy) throw new InvalidOperationException(L.T("s92f637408f"));
        var failures = new List<string>();
        await Task.WhenAll(targets.Select(async request =>
        {
            var target = request.Display;
            try
            {
                await WriteBrightnessAsync(target.Id, d => BrightnessRules.Adjust(request.Value, d.Brightness, d.Dimming),
                    transition: brightnessTransitionsEnabled && System.Windows.SystemParameters.ClientAreaAnimation);
            }
            catch (Exception ex)
            {
                failures.Add(target.Name); Storage.Log(ex.ToString());
            }
        }));
        if (failures.Count > 0) throw new InvalidOperationException(L.T("s0ed98f4c87") + string.Join("、", failures) + L.T("s335ece13e6"));
        var levels = targets.Select(t => GetCombinedBrightness(t.Display)).Distinct().ToArray();
        string scope = targets.Count == 1 ? targets[0].Display.Name : L.F("s57a83fca96", targets.Count);
        Status(L.T("s8e2f643ad1") + " " + (levels.Length == 1 ? levels[0] + "%" : L.T("quick.multiMixed")) + " · " + scope);
    }

    private DisplayInfo CurrentBrightnessDisplay(string id) =>
        Displays.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(L.T("scene.disconnected"));

    private Task RestoreBrightnessAsync(DisplayInfo display, PresetDisplay saved, CancellationToken cancellation = default) =>
        WriteBrightnessAsync(display.Id, d =>
        {
            if (saved.Brightness is < 0 or > 100) throw new InvalidOperationException(L.T("group.invalid"));
            if (saved.Brightness.HasValue && !d.Brightness.HasValue) throw new InvalidOperationException(L.T("scene.unsupported"));
            return new(saved.Brightness ?? d.Brightness, Math.Clamp(saved.Dimming, 0, BrightnessRules.MaxDimming));
        }, cancellation);

    private async Task WriteBrightnessAsync(string id, Func<DisplayInfo, CombinedLevel> plan, CancellationToken cancellation = default,
        bool transition = false)
    {
        // Slider, keyboard, restore and reset operations can overlap at a slow DDC write.
        // Plan from the last completed pair while holding this display's gate.
        if (!brightnessGates.TryGetValue(id, out var gate)) brightnessGates[id] = gate = new(1, 1);
        await gate.WaitAsync(cancellation);
        try
        {
            var display = CurrentBrightnessDisplay(id);
            var level = plan(display);
            async Task WriteLevel(CombinedLevel next)
            {
                var token = transition ? brightnessTransitionLifetime.Token : cancellation;
                token.ThrowIfCancellationRequested();
                if (next.Hardware is int hardware && display.Brightness != hardware)
                {
                    await Service.SetPercentAsync(display, 0x10, hardware, token);
                    display.Brightness = hardware;
                    // Keep the actual backlight even if a later overlay change fails.
                    if (!cancellation.CanBeCanceled && next.Dimming != display.Dimming) RememberBrightness(display);
                }
                token.ThrowIfCancellationRequested();
                if (next.Dimming != display.Dimming) SetDimming(display, next.Dimming);
                RememberBrightness(display);
            }
            await BrightnessTransition.ApplyAsync(new(display.Brightness, display.Dimming), level, transition, WriteLevel,
                transition ? brightnessTransitionLifetime.Token : cancellation);
            BrightnessChanged?.Invoke(display);
        }
        finally { gate.Release(); }
    }

    private async Task ResetDimmingAsync()
    {
        if (sceneBusy) throw new InvalidOperationException(L.T("s92f637408f"));
        await Task.WhenAll(Displays.Select(d =>
        {
            CancelReconnect(d.Id);
            return WriteBrightnessAsync(d.Id, current => new(current.Brightness, 0));
        }));
        RefreshView(); quick?.Render();
        Status(L.T("s4499bbc369"));
    }
}

internal sealed record BrightnessTarget(DisplayInfo Display, int Value);
