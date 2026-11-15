// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestSceneNoteFixture(MainWindow owner)
    {
        var scene = new Preset
        {
            Name = "Work", OnConnect = true, AtTime = "09:00", OnPower = "ac", Shortcut = new() { Key = 0x57 },
            Displays = new() { new() { Id = "disconnected-fixture", Brightness = 25 } }
        };
        string original = JsonSerializer.Serialize(scene, Storage.Json);
        string folder = Storage.Folder;
        var previous = owner.Preferences.Presets;
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        TextBox Editor() => Descendants<TextBox>(owner.PageContent).Single(t => AutomationProperties.GetAutomationId(t) == "SceneNoteEditor");
        Button SaveNote() => Descendants<Button>(owner.PageContent).Single(b => b.Content as string == L.T("scene.saveNote"));
        TextBlock[] Previews() => Descendants<TextBlock>(owner.PageContent).Where(t => AutomationProperties.GetAutomationId(t) == "SceneNotePreview").ToArray();
        try
        {
            owner.Preferences.Presets = new() { scene };
            Storage.Save(owner.Preferences);
            Click(navigation[5]);
            Check(Editor().MaxLength == 500 && Editor().AcceptsReturn && Editor().TextWrapping == TextWrapping.Wrap &&
                AutomationProperties.GetName(Editor()) == "Work: " + L.T("scene.note") && Previews().Length == 0,
                "Preset page exposes an accessible bounded multiline note editor and hides empty previews");
            const string memo = "Evening reading\r\n阅读 🌙";
            Editor().Text = "  " + memo + "  ";
            Check(scene.Note == null && Storage.Load().Presets.Single().Note == null,
                "Typing a note does not change the saved preset before Save note");
            Click(SaveNote());
            Check(scene.Note == memo && Storage.Load().Presets.Single().Note == memo && Previews().Single().Text == memo,
                "Save note persists and previews multiline text with no connected screens");
            Click(navigation[0]); Click(navigation[5]);
            Check(Editor().Text == memo && Previews().Single().ToolTip as string == memo,
                "Reopening presets shows the saved note in the editor and preview tooltip");

            string blockedFolder = Path.Combine(folder, "blocked-note-directory");
            File.WriteAllText(blockedFolder, "fixture");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", blockedFolder);
            Editor().Text = "Retry this note"; Click(SaveNote());
            Check(scene.Note == memo && Editor().Text == "Retry this note" && owner.StatusLabel.Text.StartsWith(L.T("s888403f2f3")),
                "Failed note persistence restores the model and keeps typed text available for retry");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", folder);
            Check(Storage.Load().Presets.Single().Note == memo, "Failed note save preserves the previously stored note");
            Click(SaveNote());
            Check(Storage.Load().Presets.Single().Note == "Retry this note", "A failed note save can be retried");
            Editor().Text = " \r\n "; Click(SaveNote());
            Check(Previews().Length == 0 && Editor().Text == "" &&
                JsonSerializer.Serialize(Storage.Load().Presets.Single(), Storage.Json) == original,
                "Clearing a note removes its preview and preserves identity, display settings and automation");
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
