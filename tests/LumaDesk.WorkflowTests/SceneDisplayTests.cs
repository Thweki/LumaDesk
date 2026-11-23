// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneDisplayTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var backend = new FakeBackend();
        var source = await SceneService.Capture(backend, "Work");
        source.OnConnect = true; source.AtTime = "09:00"; source.Shortcut = new() { Key = 0x57 };
        var saved = source.Displays[0];
        saved.Mode = new(1080, 1920, 60); saved.Orientation = 1; saved.Dpi = 150;
        saved.Hdr = true; saved.Brightness = 25; saved.Dimming = 15;
        string original = JsonSerializer.Serialize(source, Storage.Json);
        var selected = SceneService.ForDisplay(source, "a");
        var single = selected.Displays.Single();
        check(single.Id == "A" && single.Mode == saved.Mode && single.Orientation == 1 && single.Dpi == 150 &&
            single.Hdr == true && single.UserProfiles == saved.UserProfiles && single.SdrProfile == saved.SdrProfile &&
            single.HdrProfile == saved.HdrProfile && single.Brightness == 25 && single.Dimming == 15,
            "Single-display selection preserves every saved device setting and matches IDs ignoring case");
        check(single.X == null && single.Y == null && single.Primary == null,
            "Single-display selection excludes desktop coordinates and primary-screen changes");
        check(!selected.OnConnect && selected.AtTime == null && selected.Shortcut == null,
            "Transient single-display selection cannot inherit automatic triggers");
        var result = await SceneService.Apply(backend, selected);
        check(result.Applied == 1 && result.Issues.Count == 0 && backend.Calls.SequenceEqual(new[]
            { "mode:A", "dpi:A", "hdr:A", "profiles:A", "brightness:A" }),
            "Applying one display performs ordered settings writes only for that display, without a layout warning");
        check(backend.StaleReferences == 0, "Single-display apply resolves fresh objects after mode changes");
        single.Brightness = 80;
        check(JsonSerializer.Serialize(source, Storage.Json) == original,
            "Selecting, applying and editing a transient selection never changes the saved preset or automation");

        foreach (string id in new[] { "", " ", "missing", "1", "Work" })
        {
            bool rejected = false;
            try { SceneService.ForDisplay(source, id); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Invalid selection never falls back to another screen: " + id);
        }
        source.Displays.Add(new() { Id = "a" });
        bool ambiguous = false;
        try { SceneService.ForDisplay(source, "A"); } catch (ArgumentException) { ambiguous = true; }
        check(ambiguous, "Duplicate saved identities are rejected before any single-display writes");
        source.Displays.RemoveAt(source.Displays.Count - 1);

        backend = new FakeBackend(); backend.Screens.RemoveAt(1);
        result = await SceneService.Apply(backend, SceneService.ForDisplay(source, "A"));
        check(result.Applied == 1 && result.Issues.Count == 0,
            "Unselected disconnected screens do not prevent applying the selected screen");
        backend.Calls.Clear(); backend.Screens.Clear();
        backend.Screens.Add(new() { Id = "replacement", Name = "A", Number = 1 });
        result = await SceneService.Apply(backend, SceneService.ForDisplay(source, "A"));
        check(result.Applied == 0 && result.Issues.Count == 1 && backend.Calls.Count == 0,
            "A disconnected target is reported without retargeting a same-name replacement");

        backend = new FakeBackend(); backend.FailBrightness.Add("A");
        result = await SceneService.Apply(backend, SceneService.ForDisplay(source, "A"));
        check(result.Applied == 0 && result.Issues.Count == 1 && backend.Calls.All(c => c.EndsWith(":A")),
            "Partial failure is reported without applying other screens or desktop layout");

        backend = new FakeBackend();
        var before = SceneService.ForDisplay(await SceneService.Capture(backend, "Before"), "A");
        await SceneService.Apply(backend, SceneService.ForDisplay(source, "A"));
        backend.Calls.Clear();
        result = await SceneService.Apply(backend, before);
        check(result.Applied == 1 && result.Issues.Count == 0 && backend.Calls.All(c => c.EndsWith(":A")) &&
            backend.Screens[0].Current == new DisplayMode(1920, 1080, 60) && backend.Screens[0].Orientation == 0,
            "Scoped undo restores the selected screen's original mode without writes to another screen or layout");

        var legacy = new Preset { Displays = new() { new() { Id = "A", Brightness = 30 } } };
        backend.Calls.Clear();
        await SceneService.Apply(backend, SceneService.ForDisplay(legacy, "A"));
        check(backend.Calls.SequenceEqual(new[] { "brightness:A" }),
            "Single-display legacy presets keep unspecified settings untouched");
        backend = new FakeBackend(); backend.Screens.RemoveAt(1);
        var only = await SceneService.Capture(backend, "Only"); only.Displays[0].X = 100;
        result = await SceneService.Apply(backend, SceneService.ForDisplay(only, "A"));
        check(result.Issues.Count == 0 && !backend.Calls.Contains("layout"),
            "Even a one-monitor desktop never receives saved layout from single-display apply");
    }
}
