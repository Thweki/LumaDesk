// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneApplyPreviewTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        string? flag = Environment.GetEnvironmentVariable(SceneApplyPreview.Flag);
        try
        {
            foreach (string? value in new string?[] { null, "", "0", "true", "1" })
            {
                Environment.SetEnvironmentVariable(SceneApplyPreview.Flag, value);
                check(SceneApplyPreview.Enabled == (value == "1"), "Apply preview requires explicit opt-in: " + (value ?? "unset"));
            }
        }
        finally { Environment.SetEnvironmentVariable(SceneApplyPreview.Flag, flag); }

        var backend = new FakeBackend();
        var preset = await SceneService.Capture(backend, "Office");
        preset.Displays.Add(new() { Id = "a", Name = "duplicate", Hdr = true });
        preset.Displays.Add(new() { Id = "missing", Name = "Dock screen" });
        backend.Screens[0].Name = "Desk alias"; backend.Screens[0].Number = 2;
        backend.Screens[0].Id = "a";
        string saved = JsonSerializer.Serialize(preset, Storage.Json);
        string live = JsonSerializer.Serialize(backend.Screens, Storage.Json);
        var preview = new SceneApplyPreview(preset, backend.Displays);
        check(preview.ConnectedCount == 2 && preview.Targets.Count == 3 && preview.HasLayout,
            "Preview matches IDs case-insensitively and ignores duplicate saved targets like apply");
        check(preview.Targets[0].Name == "2 · Desk alias" && !preview.Targets[2].Connected && preview.Targets[2].Name == "Dock screen",
            "Preview identifies connected displays by current alias and number, and missing ones by saved name");
        check(preview.Targets[0].Settings.Contains("HDR") && preview.Targets[0].Settings.Contains("ICC") &&
            preview.Targets[0].Settings.Contains(L.T("scene.scale")), "Full preset scope includes optional restoration groups");
        var legacy = new Preset { Displays = new() { new() { Id = "A", Brightness = 30, Orientation = 1, SdrProfile = "ignored.icc" } } };
        var legacyPreview = new SceneApplyPreview(legacy, backend.Displays);
        check(!legacyPreview.HasLayout && legacyPreview.Targets.Single().Settings == L.T("scene.previewBrightness"),
            "Legacy scope omits unspecified groups and fields apply ignores without their parent setting");
        check(JsonSerializer.Serialize(preset, Storage.Json) == saved && JsonSerializer.Serialize(backend.Screens, Storage.Json) == live && backend.Calls.Count == 0,
            "Preview construction does not mutate presets, live settings, or issue hardware writes");

        int scans = 0, confirmations = 0;
        Task<IReadOnlyList<DisplayInfo>> Scan() { scans++; return Task.FromResult(backend.Displays); }
        bool canceled = await SceneApplyPreview.ConfirmAsync(preset, Scan, p => { confirmations++; return false; });
        check(!canceled && scans == 1 && confirmations == 1 && backend.Calls.Count == 0,
            "Cancel stops after the preview scan without writes or another scan");
        scans = 0;
        check(await SceneApplyPreview.ConfirmAsync(preset, Scan, p => true) && scans == 2 && backend.Calls.Count == 0,
            "Confirmation rechecks connections and authorizes apply without writing during preview");
        check(await SceneApplyPreview.ConfirmAsync(preset, Scan, p =>
        {
            backend.Screens.Reverse(); backend.Screens[0].Id = backend.Screens[0].Id.ToLowerInvariant();
            return true;
        }), "Enumeration order and ID casing changes do not invalidate the same connected displays");
        scans = 0;
        check(!await SceneApplyPreview.ConfirmAsync(new Preset(), Scan, p => true) && scans == 1,
            "Even a confirming caller cannot approve an empty preset");
        check(!await SceneApplyPreview.ConfirmAsync(new Preset { Displays = new() { new() { Id = "absent" } } }, Scan, p => true),
            "A fully disconnected preset cannot be approved");

        foreach (bool add in new[] { true, false })
        {
            backend = new FakeBackend();
            bool rejected = false;
            try
            {
                await SceneApplyPreview.ConfirmAsync(preset, Scan, p =>
                {
                    if (add) backend.Screens.Add(new() { Id = "extra" });
                    else backend.Screens.RemoveAt(1);
                    return true;
                });
            }
            catch (InvalidOperationException ex) { rejected = ex.Message == L.T("scene.previewChanged"); }
            check(rejected && backend.Calls.Count == 0, "Connection changes invalidate confirmation, including extra displays: " + add);
        }
        bool scanFailed = false;
        try
        {
            await SceneApplyPreview.ConfirmAsync(preset,
                () => Task.FromException<IReadOnlyList<DisplayInfo>>(new InvalidOperationException("scan failed")),
                p => throw new Exception("A failed scan must not open confirmation"));
        }
        catch (InvalidOperationException ex) { scanFailed = ex.Message == "scan failed"; }
        check(scanFailed, "Scan failure never falls back to a stale preview");
        scans = 0; scanFailed = false;
        try
        {
            await SceneApplyPreview.ConfirmAsync(preset, () => ++scans == 1 ? Task.FromResult(backend.Displays) :
                Task.FromException<IReadOnlyList<DisplayInfo>>(new InvalidOperationException("rescan failed")), p => true);
        }
        catch (InvalidOperationException ex) { scanFailed = ex.Message == "rescan failed"; }
        check(scanFailed && backend.Calls.Count == 0, "A failed confirmation rescan cannot authorize application");
    }
}
