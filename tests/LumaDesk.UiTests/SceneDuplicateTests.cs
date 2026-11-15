// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestSceneDuplicateFixture(MainWindow owner)
    {
        var source = new Preset
        {
            Name = "Work", OnConnect = true, AtTime = "09:00", OnPower = "ac", Shortcut = new() { Key = 0x57 },
            Displays = new() { new() { Id = "disconnected-fixture", Brightness = 25, Dimming = 0.2 } }
        };
        string original = JsonSerializer.Serialize(source, Storage.Json);
        string folder = Storage.Folder;
        var previous = owner.Preferences.Presets;
        try
        {
            owner.Preferences.Presets = new() { source };
            Storage.Save(owner.Preferences);
            var editor = owner.CreateSceneDuplicateEditor(source);
            var name = Descendants<TextBox>(editor).Single();
            var button = Descendants<Button>(editor).Single();
            Check(name.MaxLength == 40 && button.Content.ToString() == L.T("scene.duplicate"),
                "Preset duplicate editor exposes a named action and bounded name input");
            Click(button);
            Check(owner.Preferences.Presets.Count == 1 && owner.StatusLabel.Text == L.T("scene.duplicateNameRequired"),
                "Empty duplicate name reports validation without creating a preset");
            name.Text = " work "; Click(button);
            Check(owner.Preferences.Presets.Count == 1 && owner.StatusLabel.Text == L.T("s1f09d8e896"),
                "Duplicate editor rejects case-insensitive name collisions");
            name.Text = "Evening"; Click(button);
            var saved = Storage.Load();
            var copy = saved.Presets.Single(p => p.Name == "Evening");
            Check(saved.Presets.Count == 2 && copy.Id != source.Id && copy.Displays.Single().Id == "disconnected-fixture" &&
                copy.Displays[0].Brightness == 25 && copy.Displays[0].Mode == null,
                "Duplicate button persists a new preset from saved values with no connected displays");
            Check(JsonSerializer.Serialize(saved.Presets.Single(p => p.Id == source.Id), Storage.Json) == original &&
                !copy.OnConnect && copy.AtTime == null && copy.OnPower == null && copy.Shortcut == null,
                "Duplicate UI preserves original automation and leaves copy triggers off");
            Click(button);
            Check(owner.Preferences.Presets.Count == 2 && Storage.Load().Presets.Count == 2,
                "Repeated duplicate submission cannot create a second preset with the same name");

            // A file cannot serve as the settings directory, even under elevated test users.
            string blockedFolder = Path.Combine(folder, "blocked-directory");
            File.WriteAllText(blockedFolder, "fixture");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", blockedFolder);
            name.Text = "Unsaved"; Click(button);
            Check(owner.Preferences.Presets.Count == 2 && owner.StatusLabel.Text.StartsWith(L.T("s888403f2f3")),
                "Failed duplicate persistence removes the unsaved copy and reports the failure");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", folder);
            Check(Storage.Load().Presets.Count == 2 && JsonSerializer.Serialize(source, Storage.Json) == original,
                "Failed duplicate persistence preserves saved presets and the source");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", folder);
            owner.Preferences.Presets = previous;
            Storage.Save(owner.Preferences);
        }
    }
}
