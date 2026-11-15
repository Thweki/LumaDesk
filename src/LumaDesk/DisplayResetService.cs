// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

[Flags]
internal enum DisplayResetFields { Brightness = 1, Scale = 2, Rotation = 4 }

internal sealed record DisplayResetBaseline(string PresetName, int? Brightness, double Dimming, int? Dpi, uint? Orientation)
{
    internal DisplayResetFields Available => DisplayResetFields.Brightness |
        (Dpi.HasValue ? DisplayResetFields.Scale : 0) | (Orientation.HasValue ? DisplayResetFields.Rotation : 0);
}

// Session-only, immutable values: editing/deleting a preset cannot rewrite what was applied.
internal sealed class DisplayResetService
{
    private readonly Dictionary<string, DisplayResetBaseline> baselines = new(StringComparer.OrdinalIgnoreCase);
    internal DisplayResetBaseline? Baseline(string id) => baselines.GetValueOrDefault(id);

    internal void Remember(Preset preset, SceneResult result)
    {
        foreach (var saved in preset.Displays.DistinctBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
            if (result.SuccessfulDisplayIds.Contains(saved.Id))
                baselines[saved.Id] = new(preset.Name, saved.Brightness,
                    Math.Clamp(saved.Dimming, 0, BrightnessRules.MaxDimming), saved.Dpi,
                    saved.Mode != null ? saved.Orientation : null);
    }

    internal async Task<SceneResult> Reset(ISceneBackend backend, string id, DisplayResetFields fields)
    {
        var baseline = Baseline(id) ?? throw new InvalidOperationException(L.T("reset.empty"));
        if (fields == 0 || (fields & ~baseline.Available) != 0)
            throw new InvalidOperationException(L.T("scene.unsupported"));
        var result = new SceneResult();
        DisplayInfo Find() => backend.Displays.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(L.T("scene.disconnected"));
        async Task Step(string label, Func<DisplayInfo, Task> action)
        {
            try { await action(Find()); }
            catch (Exception ex) { result.Issues.Add(label + ": " + ex.Message); }
        }

        // Rotation preserves the current pixel dimensions (swapped for portrait) and refresh rate.
        // Re-resolve after mode changes because the backend can replace every display object.
        if (fields.HasFlag(DisplayResetFields.Rotation)) await Step(L.T("s60cc99cc00"), async d =>
        {
            uint orientation = baseline.Orientation!.Value;
            if (orientation > 3) throw new InvalidOperationException(L.T("scene.unsupported"));
            if (d.Orientation == orientation) return;
            var mode = (d.Orientation % 2) == (orientation % 2) ? d.Current :
                new DisplayMode(d.Current.Height, d.Current.Width, d.Current.Frequency);
            await backend.Mode(d, mode, orientation);
        });
        if (fields.HasFlag(DisplayResetFields.Scale)) await Step(L.T("scene.scale"), d =>
            d.DpiScale.CurrentPercent == baseline.Dpi ? Task.CompletedTask : backend.Dpi(d, baseline.Dpi!.Value));
        if (fields.HasFlag(DisplayResetFields.Brightness)) await Step(L.T("scene.brightness"), d =>
            (!baseline.Brightness.HasValue || d.Brightness == baseline.Brightness) && d.Dimming == baseline.Dimming
                ? Task.CompletedTask : backend.Brightness(d, new PresetDisplay
                { Id = id, Brightness = baseline.Brightness, Dimming = baseline.Dimming }));
        if (result.Issues.Count == 0) { result.Applied = 1; result.SuccessfulDisplayIds.Add(id); }
        return result;
    }
}
