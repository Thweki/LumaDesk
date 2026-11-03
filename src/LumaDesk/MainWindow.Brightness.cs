// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

public partial class MainWindow
{
    internal int GetCombinedBrightness(DisplayInfo display) => BrightnessRules.Combine(display.Brightness, display.Dimming);
    internal async Task ChangeCombinedBrightness(DisplayInfo display, int value)
    {
        var targets = BrightnessTargets(display);
        await ApplyCombinedBrightness(targets.Select(target => new BrightnessTarget(target,
            GroupRules.Target(value, Options(display), Options(target)))).ToArray());
        Status(L.F("sb85c114a54", value) + (targets.Count == 1 ? display.Name : L.F("s57a83fca96", targets.Count)));
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
        Status(L.F("sb85c114a54", value) + L.F("s57a83fca96", targets.Count));
    }

    private async Task ApplyCombinedBrightness(IReadOnlyList<BrightnessTarget> targets)
    {
        if (sceneBusy) throw new InvalidOperationException(L.T("s92f637408f"));
        foreach (var request in targets) CancelReconnect(request.Display.Id);
        var failures = new List<string>();
        await Task.WhenAll(targets.Select(async request =>
        {
            var target = request.Display;
            int previousDimming = target.Dimming;
            var level = BrightnessRules.Split(request.Value, target.Brightness.HasValue);
            try
            {
                if (level.Dimming > previousDimming) SetDimming(target, level.Dimming);
                if (level.Hardware.HasValue && target.Brightness != level.Hardware)
                {
                    await Service.SetPercentAsync(target, 0x10, level.Hardware.Value);
                    target.Brightness = level.Hardware;
                }
                SetDimming(target, level.Dimming);
            }
            catch (Exception ex)
            {
                try { SetDimming(target, previousDimming); } catch (Exception restore) { Storage.Log(restore.Message); }
                failures.Add(target.Name); Storage.Log(ex.ToString());
            }
        }));
        if (failures.Count > 0) throw new InvalidOperationException(L.T("s0ed98f4c87") + string.Join("、", failures) + L.T("s335ece13e6"));
    }
}

internal sealed record BrightnessTarget(DisplayInfo Display, int Value);
