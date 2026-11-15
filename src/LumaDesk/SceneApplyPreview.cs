// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

internal sealed record SceneApplyTarget(string Id, string Name, bool Connected, string Settings);

// An application scope, not a value comparison: even matching brightness and ICC
// settings are passed to the existing restore pipeline.
internal sealed class SceneApplyPreview
{
    internal const string Flag = "LUMADESK_EXPERIMENTAL_PRESET_APPLY_PREVIEW";
    internal static bool Enabled => Environment.GetEnvironmentVariable(Flag) == "1";
    private readonly HashSet<string> inventory;
    internal IReadOnlyList<SceneApplyTarget> Targets { get; }
    internal int ConnectedCount => Targets.Count(t => t.Connected);
    internal bool HasLayout { get; }

    internal SceneApplyPreview(Preset preset, IReadOnlyList<DisplayInfo> displays)
    {
        inventory = new(displays.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
        HasLayout = preset.Displays.Any(d => d.X.HasValue);
        var targets = new List<SceneApplyTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var saved in preset.Displays)
        {
            if (!seen.Add(saved.Id)) continue;
            var display = displays.FirstOrDefault(d => string.Equals(d.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
            var settings = new List<string> { L.T("scene.previewBrightness") };
            if (saved.Mode != null) settings.Add(L.T("scene.mode"));
            if (saved.Dpi.HasValue) settings.Add(L.T("scene.scale"));
            if (saved.Hdr.HasValue) settings.Add("HDR");
            if (saved.UserProfiles.HasValue) settings.Add("ICC");
            string? name = display?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = string.IsNullOrWhiteSpace(saved.Name) ? saved.Id : saved.Name;
            if (display?.Number > 0) name = display.Number + " · " + name;
            targets.Add(new(saved.Id, name, display != null, string.Join(" · ", settings)));
        }
        Targets = targets.AsReadOnly();
    }

    internal static async Task<bool> ConfirmAsync(Preset preset,
        Func<Task<IReadOnlyList<DisplayInfo>>> scan, Func<SceneApplyPreview, bool> confirm)
    {
        var preview = new SceneApplyPreview(preset, await scan());
        if (!confirm(preview) || preview.ConnectedCount == 0) return false;
        // A dock can change while the dialog is open. Require a new preview then.
        if (!preview.inventory.SetEquals((await scan()).Select(d => d.Id)))
            throw new InvalidOperationException(L.T("scene.previewChanged"));
        return true;
    }
}
