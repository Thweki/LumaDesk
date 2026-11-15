// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

internal static partial class Program
{
    private static void TestSceneRenameFixture(MainWindow owner)
    {
        // Walk logical controls so the owner stays unloaded and never scans hardware.
        IEnumerable<T> Controls<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is T control) yield return control;
                foreach (var descendant in Controls<T>(child)) yield return descendant;
            }
        }
        var scene = new Preset
        {
            Name = "Work", OnConnect = true, AtTime = "21:30", OnPower = "battery",
            Shortcut = new() { Modifiers = 3, Key = 0x57 },
            Displays = new() { new() { Id = "disconnected-screen", Brightness = 27, Dimming = 10, Mode = new(1920, 1080, 60) } }
        };
        owner.Preferences.Presets.Add(scene);
        owner.Preferences.Presets.Add(new Preset { Name = "Night" });
        Storage.Save(owner.Preferences);
        string Encode(Preferences value) => JsonSerializer.Serialize(value, Storage.Json);
        var expected = JsonSerializer.Deserialize<Preferences>(Encode(owner.Preferences), Storage.Json)!;
        expected.Presets[0].Name = "办公";
        Click(owner.Navigation.Children.OfType<Button>().Single(b => b.Tag as string == L.T("sf1de1e2621")));
        TextBox NameBox() => Controls<TextBox>(owner.PageContent).First(b => AutomationProperties.GetName(b) == L.T("s2c09021a16"));
        Button RenameButton() => Controls<Button>(owner.PageContent).First(b => b.Content as string == L.T("scene.rename"));
        Check(Controls<Button>(owner.PageContent).Any(b => b.Content as string == L.T("scene.update")),
            "Preset editor offers rename separately from capturing the current setup");
        NameBox().Text = "  办公  "; Click(RenameButton());
        Check(owner.Displays.Count == 0 && ReferenceEquals(owner.Preferences.Presets[0], scene) && Encode(owner.Preferences) == Encode(expected),
            "Rename button works with disconnected displays and preserves the saved setup and bindings");
        Check(Encode(Storage.Load()) == Encode(expected), "Rename button persists the new name and unchanged preset ID across reload");
        Check(Controls<TextBlock>(owner.PageContent).Any(t => t.Text == "办公") && NameBox().Text == "办公" && owner.StatusLabel.Text == L.T("scene.renamed"),
            "Rename refreshes the card heading, editor and success feedback");
        foreach (string invalid in new[] { "   ", " nIgHt " })
        {
            NameBox().Text = invalid; Click(RenameButton());
            Check(Encode(Storage.Load()) == Encode(expected) && scene.Name == "办公" && NameBox().Text == invalid,
                "Rejected rename leaves persistence unchanged and keeps the entered name for correction");
        }

        // A failed disk write must not claim success or change the live preset name.
        string blockedPath = Path.Combine(Storage.Folder, "settings.json.tmp");
        Directory.CreateDirectory(blockedPath);
        try
        {
            NameBox().Text = "Unsaved"; Click(RenameButton());
            Check(scene.Name == "办公" && Encode(Storage.Load()) == Encode(expected) && owner.StatusLabel.Text != L.T("scene.renamed"),
                "Failed rename persistence restores the live name and reports an error");
        }
        finally { Directory.Delete(blockedPath); }
        owner.Preferences.Presets.Clear();
        Storage.Save(owner.Preferences);
    }
}
