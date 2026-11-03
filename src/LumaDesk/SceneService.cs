// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

internal interface ISceneBackend
{
    IReadOnlyList<DisplayInfo> Displays { get; }
    Task<DisplayColorState> Color(DisplayInfo display);
    Task Mode(DisplayInfo display, DisplayMode mode, uint orientation);
    Task Dpi(DisplayInfo display, int percent);
    Task Hdr(DisplayInfo display, bool enabled);
    Task Profiles(DisplayInfo display, PresetDisplay saved);
    Task Brightness(DisplayInfo display, PresetDisplay saved);
    Task Layout(IReadOnlyList<ScreenPlacement> placements);
}

internal sealed class SceneResult
{
    public int Applied { get; set; }
    public List<string> Issues { get; } = new();
}

internal static class SceneService
{
    internal static async Task<Preset> Capture(ISceneBackend backend, string name)
    {
        var scene = new Preset { Name = name };
        foreach (var d in backend.Displays.ToArray())
        {
            var saved = new PresetDisplay { Id = d.Id, Name = d.Name, Brightness = d.Brightness, Dimming = d.Dimming,
                Mode = d.Current, Orientation = d.Orientation, Dpi = d.DpiScale.CanSet ? d.DpiScale.CurrentPercent : null,
                X = d.X, Y = d.Y, Primary = d.Primary };
            try
            {
                var color = await backend.Color(d);
                if (color.HdrKnown && color.HdrSupported) saved.Hdr = color.HdrUserEnabled;
                if (color.ProfilesAvailable)
                {
                    saved.UserProfiles = color.UseUserProfiles;
                    saved.SdrProfile = color.SdrProfile; saved.HdrProfile = color.HdrProfile;
                }
            }
            catch (Exception ex) { Storage.Log("Scene color capture: " + ex.Message); }
            scene.Displays.Add(saved);
        }
        return scene;
    }

    // Resolve the device again between operations: a mode change can replace every DisplayInfo.
    internal static async Task<SceneResult> Apply(ISceneBackend backend, Preset scene)
    {
        var result = new SceneResult();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var saved in scene.Displays)
        {
            if (!ids.Add(saved.Id)) continue;
            DisplayInfo? Find() => backend.Displays.FirstOrDefault(d => string.Equals(d.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
            var display = Find();
            string name = display?.Name ?? (string.IsNullOrEmpty(saved.Name) ? saved.Id : saved.Name);
            if (display == null) { result.Issues.Add(name + ": " + L.T("scene.disconnected")); continue; }
            bool failed = false;
            async Task Step(string label, Func<DisplayInfo, Task> action)
            {
                try { await action(Find() ?? throw new InvalidOperationException(L.T("scene.disconnected"))); }
                catch (Exception ex) { failed = true; result.Issues.Add(name + " · " + label + ": " + ex.Message); }
            }
            if (saved.Mode != null) await Step(L.T("scene.mode"), async d =>
            {
                uint orientation = saved.Orientation ?? d.Orientation;
                if (d.Current == saved.Mode && d.Orientation == orientation) return;
                await backend.Mode(d, saved.Mode, orientation);
            });
            if (saved.Dpi.HasValue) await Step(L.T("scene.scale"), d => saved.Dpi == d.DpiScale.CurrentPercent ? Task.CompletedTask : backend.Dpi(d, saved.Dpi.Value));
            if (saved.Hdr.HasValue) await Step("HDR", async d =>
            {
                var state = await backend.Color(d);
                if (!state.HdrKnown || !state.HdrSupported) throw new InvalidOperationException(L.T("scene.unsupported"));
                if (state.HdrUserEnabled != saved.Hdr) await backend.Hdr(d, saved.Hdr.Value);
            });
            if (saved.UserProfiles.HasValue) await Step("ICC", d => backend.Profiles(d, saved));
            await Step(L.T("scene.brightness"), d => backend.Brightness(d, saved));
            if (!failed) result.Applied++;
        }
        if (scene.Displays.Any(d => d.X.HasValue))
        {
            var layout = LayoutFor(scene, backend.Displays);
            if (layout == null) result.Issues.Add(L.T("scene.layoutSkipped"));
            else if (layout.Any(p => { var d = backend.Displays.First(d => d.Id == p.Id); return d.X != p.X || d.Y != p.Y || d.Primary != p.Primary; }))
            {
                try { await backend.Layout(layout); }
                catch (Exception ex) { result.Issues.Add(L.T("scene.layout") + ": " + ex.Message); }
            }
        }
        return result;
    }

    internal static bool SameScreens(Preset scene, IEnumerable<DisplayInfo> displays) => scene.Displays.Count > 0 &&
        new HashSet<string>(scene.Displays.Select(d => d.Id), StringComparer.OrdinalIgnoreCase).SetEquals(displays.Select(d => d.Id));

    internal static List<ScreenPlacement>? LayoutFor(Preset scene, IReadOnlyList<DisplayInfo> displays)
    {
        if (!SameScreens(scene, displays) || scene.Displays.Count != displays.Count) return null;
        var placements = new List<ScreenPlacement>();
        foreach (var saved in scene.Displays)
        {
            var d = displays.First(d => string.Equals(d.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
            if (!saved.X.HasValue || !saved.Y.HasValue || !saved.Primary.HasValue || saved.Mode != d.Current || saved.Orientation != d.Orientation) return null;
            placements.Add(new(d.Id, saved.X.Value, saved.Y.Value, (int)d.Current.Width, (int)d.Current.Height, saved.Primary.Value));
        }
        try { return LayoutRules.Normalize(placements); } catch (InvalidOperationException) { return null; }
    }
}
