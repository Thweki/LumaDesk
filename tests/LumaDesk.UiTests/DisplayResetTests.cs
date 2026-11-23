// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestDisplayResetFixture(MainWindow owner)
    {
        var display = new DisplayInfo { Id = "reset-fixture", Name = "Fixture" };
        var calls = new List<(string, DisplayResetFields)>();
        Task Reset(string id, DisplayResetFields fields) { calls.Add((id, fields)); return Task.CompletedTask; }
        var empty = owner.CreateDisplayResetControls(display, Reset);
        Check(!Descendants<Button>(empty).Any() && Descendants<TextBlock>(empty).Any(t => t.Text == L.T("reset.empty")),
            "Display reset explains the missing session baseline without offering a guessed default");

        var applied = new SceneResult { Applied = 1 }; applied.SuccessfulDisplayIds.Add(display.Id);
        owner.DisplayResets.Remember(new Preset { Name = "Work", Displays = new()
        {
            new() { Id = display.Id, Brightness = 30, Dpi = 150, Mode = new(1920, 1080, 60), Orientation = 0 }
        } }, applied);
        var controls = owner.CreateDisplayResetControls(display, Reset);
        var buttons = Descendants<Button>(controls).ToArray();
        Check(buttons.Length == 4 && buttons.All(b => b.IsEnabled && !string.IsNullOrEmpty(AutomationProperties.GetName(b))) &&
            Descendants<TextBlock>(controls).Any(t => t.Text == L.F("reset.source", "Work")),
            "Reset panel names the applied preset and exposes four accessible actions");
        foreach (var button in buttons) Click(button);
        Check(calls.SequenceEqual(new[] {
            (display.Id, DisplayResetFields.Brightness), (display.Id, DisplayResetFields.Scale),
            (display.Id, DisplayResetFields.Rotation),
            (display.Id, DisplayResetFields.Brightness | DisplayResetFields.Scale | DisplayResetFields.Rotation) }),
            "Reset buttons dispatch the selected device identity and exact requested fields");

        owner.DisplayResets.Remember(new Preset { Name = "Legacy", Displays = new() { new() { Id = display.Id, Dimming = 25 } } }, applied);
        buttons = Descendants<Button>(owner.CreateDisplayResetControls(display, Reset)).ToArray();
        Check(buttons[0].IsEnabled && !buttons[1].IsEnabled && !buttons[2].IsEnabled && buttons[3].IsEnabled,
            "Legacy baseline disables scaling and rotation while keeping brightness and all-available usable");
        calls.Clear(); Click(buttons[3]);
        Check(calls.Single() == (display.Id, DisplayResetFields.Brightness),
            "All available dispatches only the fields actually saved in a legacy preset");
    }
}
