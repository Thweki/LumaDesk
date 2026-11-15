// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static async Task TestSceneDraftFixtureAsync(MainWindow owner)
    {
        string folder = Storage.Folder;
        var previous = owner.Preferences.Presets;
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        Button Action(string key) => Descendants<Button>(owner.PageContent).Single(b => b.Content as string == L.T(key));
        TextBox Name() => Descendants<TextBox>(owner.PageContent).Single(t => AutomationProperties.GetAutomationId(t) == "SceneDraftName");
        int captures = 0;
        Task<Preset> Capture()
        {
            captures++;
            return Task.FromResult(new Preset { Name = "Live", Displays = new()
            {
                new() { Id = "live-fixture", Name = "Live screen", Brightness = 31, Dimming = 0.25,
                    Mode = new(2560, 1440, 75), Dpi = 150, Hdr = true, UserProfiles = true,
                    SdrProfile = "sRGB.icm", HdrProfile = "", X = -2560, Y = 0, Primary = false }
            } });
        }
        try
        {
            owner.Preferences.Presets = new() { new() { Name = "LIVE", OnConnect = true, AtTime = "09:00" } };
            Storage.Save(owner.Preferences);
            string original = File.ReadAllText(Path.Combine(folder, "settings.json"));
            Click(navigation[5]);
            Check(Action("scene.captureDraft").IsEnabled, "Presets page exposes live capture before a draft exists");
            Click(owner.CreateSceneCaptureButton(Capture));
            Check(captures == 1 && owner.Preferences.Presets.Count == 1 &&
                File.ReadAllText(Path.Combine(folder, "settings.json")) == original,
                "Capture button creates only an unsaved draft without changing persisted presets");
            Check(Name().Text == "Live 2" && !Action("scene.captureDraft").IsEnabled,
                "Draft suggests an unused name and blocks accidental replacement");
            Name().Text = "My snapshot";
            Click(navigation[0]); Click(navigation[5]);
            Check(Name().Text == "My snapshot", "Draft and edited name survive navigation");
            Click(Action("scene.discardDraft"));
            Check(Action("scene.captureDraft").IsEnabled && File.ReadAllText(Path.Combine(folder, "settings.json")) == original,
                "Discard removes the draft without writing settings");

            // A failed capture leaves the workflow available for retry.
            bool failed = false;
            try { await owner.CaptureSceneDraftAsync(() => Task.FromException<Preset>(new InvalidOperationException("scan failed"))); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && Action("scene.captureDraft").IsEnabled && owner.IsEnabled,
                "Capture failure unlocks the window and creates no draft");
            var pending = new TaskCompletionSource<Preset>();
            var capturing = owner.CaptureSceneDraftAsync(() => pending.Task);
            await owner.CaptureSceneDraftAsync(Capture);
            Check(captures == 1, "Overlapping capture requests cannot start a second scan");
            var captured = await Capture();
            string snapshot = JsonSerializer.Serialize(captured.Displays, Storage.Json);
            pending.SetResult(captured); await capturing;
            await owner.CaptureSceneDraftAsync(Capture);
            Check(captures == 2, "An existing draft cannot be replaced by another capture request");
            var save = Action("scene.saveDraft");
            Name().Text = " "; Click(save);
            Check(owner.Preferences.Presets.Count == 1 && owner.StatusLabel.Text == L.T("scene.duplicateNameRequired"),
                "Draft save validates empty names without losing the draft");
            Name().Text = " live "; Click(save);
            Check(owner.Preferences.Presets.Count == 1 && owner.StatusLabel.Text == L.T("s1f09d8e896"),
                "Draft save checks name conflicts against saved presets");
            Name().Text = " Snapshot ";
            string blockedFolder = Path.Combine(folder, "blocked-draft-directory");
            File.WriteAllText(blockedFolder, "fixture");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", blockedFolder);
            Click(save);
            Check(owner.Preferences.Presets.Count == 1 && owner.StatusLabel.Text.StartsWith(L.T("s888403f2f3")) &&
                Name().Text == " Snapshot ", "Failed persistence rolls back the saved list and retains the named draft");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", folder);
            Check(File.ReadAllText(Path.Combine(folder, "settings.json")) == original, "Failed draft save preserves existing settings");
            Click(save);
            var saved = Storage.Load().Presets.Single(p => p.Name == "Snapshot");
            Check(captures == 2 && owner.Displays.Count == 0 && JsonSerializer.Serialize(saved.Displays, Storage.Json) == snapshot &&
                !saved.OnConnect && saved.AtTime == null && saved.OnPower == null && saved.Shortcut == null,
                "Retry saves captured values even with no connected screens, without recapture or automatic triggers");
            Check(Action("scene.captureDraft").IsEnabled, "Successful save clears the draft");
            Click(save);
            Check(Storage.Load().Presets.Count == 2, "A stale save button cannot persist the same draft twice");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", folder);
            owner.Preferences.Presets = previous;
            Storage.Save(owner.Preferences);
            Click(navigation[0]);
        }
    }
}
