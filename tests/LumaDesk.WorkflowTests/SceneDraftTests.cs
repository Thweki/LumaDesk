// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneDraftTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var backend = new FakeBackend();
        backend.Screens[0].Brightness = 31;
        backend.Screens[0].Dimming = 0.25;
        backend.Screens[1].Orientation = 1;
        backend.Screens[1].Brightness = null;
        var before = JsonSerializer.Serialize(backend.Screens, Storage.Json);
        var draft = await SceneService.CaptureDraft(backend);
        check(backend.Calls.Count == 0 && JsonSerializer.Serialize(backend.Screens, Storage.Json) == before,
            "Live draft capture does not issue any display writes or mutate live settings");
        check(draft.Displays.Select(d => d.Id).SequenceEqual(new[] { "A", "B" }) &&
            draft.Displays[0].Brightness == 31 && draft.Displays[0].Dimming == 0.25 &&
            draft.Displays[0].Mode == new DisplayMode(1920, 1080, 60) && draft.Displays[0].Dpi == 125 &&
            draft.Displays[0].Primary == true && draft.Displays[1].X == 1920 && draft.Displays[1].Y == 0 &&
            draft.Displays[1].Orientation == 1 && draft.Displays[1].Brightness == null && draft.Displays[1].Dpi == null,
            "Draft captures each live display's values and preserves unsupported settings as unspecified");
        check(draft.Displays.All(d => d.Hdr == false && d.UserProfiles == true && d.SdrProfile == "sRGB.icm"),
            "Draft includes readable HDR and ICC state");
        check(Guid.TryParseExact(draft.Id, "N", out _) && !draft.OnConnect && draft.AtTime == null &&
            draft.OnPower == null && draft.Shortcut == null, "Live draft has a fresh identity and no automatic triggers");
        var second = await SceneService.CaptureDraft(backend);
        check(second.Id != draft.Id, "Each live capture creates an independent draft");
        string snapshot = JsonSerializer.Serialize(draft.Displays, Storage.Json);
        backend.Screens[0].Brightness = 99;
        backend.Screens[0].DpiScale.CurrentPercent = 150;
        backend.Screens[0].Current = new(1280, 720, 75);
        backend.Screens.Clear();
        check(JsonSerializer.Serialize(draft.Displays, Storage.Json) == snapshot,
            "Live changes and disconnects cannot change the captured draft");
        var saved = SceneService.Duplicate(draft, "  Captured setup  ", Array.Empty<Preset>());
        check(saved.Name == "Captured setup" && JsonSerializer.Serialize(saved.Displays, Storage.Json) == snapshot && backend.Calls.Count == 0,
            "Saving a named draft uses the snapshot without recapturing or applying displays");
        bool rejected = false;
        try { await SceneService.CaptureDraft(backend); }
        catch (InvalidOperationException ex) { rejected = ex.Message == L.T("scene.draftNoDisplays"); }
        check(rejected, "An empty live inventory cannot create a misleading empty draft");
        var unavailable = await SceneService.CaptureDraft(new FakeBackend { FailColor = true });
        check(unavailable.Displays.Count == 2 && unavailable.Displays.All(d => d.Hdr == null && d.UserProfiles == null &&
            d.SdrProfile == null && d.HdrProfile == null), "Unavailable color APIs leave draft color settings unspecified");
    }
}
