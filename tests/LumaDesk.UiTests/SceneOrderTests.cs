// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestSceneOrderFixture(MainWindow owner)
    {
        var first = new Preset
        {
            Name = "Work", OnConnect = true, AtTime = "09:00", OnPower = "ac", Shortcut = new() { Key = 0x57 },
            Displays = new() { new() { Id = "disconnected-fixture", Brightness = 25, Dimming = 0.2 } }
        };
        var second = new Preset { Name = "Evening" };
        var third = new Preset { Name = "Travel" };
        string original = JsonSerializer.Serialize(first, Storage.Json);
        string folder = Storage.Folder;
        var previous = owner.Preferences.Presets;
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        Button[] Moves(string key) => Descendants<Button>(owner.PageContent).Where(b => b.Content as string == L.T(key)).ToArray();
        try
        {
            owner.Preferences.Presets = new() { first, second, third };
            Storage.Save(owner.Preferences);
            Click(navigation[5]);
            var up = Moves("panel.up"); var down = Moves("panel.down");
            Check(up.Length == 3 && down.Length == 3 && !up[0].IsEnabled && up[1].IsEnabled &&
                down[1].IsEnabled && !down[2].IsEnabled,
                "Preset cards expose move controls with first/last boundaries disabled");
            Check(AutomationProperties.GetName(down[0]) == first.Name + ": " + L.T("panel.down"),
                "Move controls identify both the preset and action to assistive technology");
            Click(down[0]);
            Check(owner.Preferences.Presets.SequenceEqual(new[] { second, first, third }) &&
                Storage.Load().Presets.Select(p => p.Id).SequenceEqual(new[] { second.Id, first.Id, third.Id }),
                "Move down button updates and persists order without connected displays");
            Check(Moves("panel.up").Select(AutomationProperties.GetName).SequenceEqual(new[] { second, first, third }.Select(p => p.Name + ": " + L.T("panel.up"))),
                "Preset cards rerender in the saved order");
            Click(Moves("panel.up")[1]);
            Check(owner.Preferences.Presets.SequenceEqual(new[] { first, second, third }) &&
                JsonSerializer.Serialize(Storage.Load().Presets[0], Storage.Json) == original,
                "Move up button restores order while preserving preset identity, settings and automation");

            string blockedFolder = Path.Combine(folder, "blocked-order-directory");
            File.WriteAllText(blockedFolder, "fixture");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", blockedFolder);
            Click(Moves("panel.down")[0]);
            Check(owner.Preferences.Presets.SequenceEqual(new[] { first, second, third }) &&
                owner.StatusLabel.Text.StartsWith(L.T("s888403f2f3")),
                "Failed reorder persistence restores the in-memory order and reports the error");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", folder);
            Check(Storage.Load().Presets.Select(p => p.Id).SequenceEqual(new[] { first.Id, second.Id, third.Id }),
                "Failed reorder leaves the previously saved order intact");
            Click(Moves("panel.down")[0]);
            Check(Storage.Load().Presets[1].Id == first.Id, "A failed reorder can be retried successfully");

            owner.Preferences.Presets = new() { first };
            Click(navigation[5]);
            Check(!Moves("panel.up").Single().IsEnabled && !Moves("panel.down").Single().IsEnabled,
                "A single preset has both move controls disabled");
            owner.Preferences.Presets.Clear(); Click(navigation[5]);
            Check(Moves("panel.up").Length == 0 && Moves("panel.down").Length == 0,
                "An empty preset list has no move controls");
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
