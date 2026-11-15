// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestSceneDisplayFixture(MainWindow owner)
    {
        var previous = owner.Displays;
        var source = new Preset { Displays = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "offline" } } };
        int calls = 0; string? requestedId = null; Preset? requestedPreset = null;
        Task Apply(Preset preset, string id) { calls++; requestedId = id; requestedPreset = preset; return Task.CompletedTask; }
        try
        {
            owner.Displays = new()
            {
                new() { Id = "b", Number = 2, Name = "Same name" },
                new() { Id = "A", Number = 1, Name = "Same name" },
                new() { Id = "not-saved", Number = 3 }
            };
            var editor = owner.CreateSceneDisplayEditor(source, Apply);
            var picker = Descendants<ComboBox>(editor).Single();
            var button = Descendants<Button>(editor).Single();
            Check(picker.Items.Count == 2 && picker.SelectedIndex == -1 && !button.IsEnabled &&
                AutomationProperties.GetName(picker) == L.T("scene.targetDisplay"),
                "Single-display editor requires explicit selection from connected saved identities");
            picker.SelectedIndex = 1; Click(button);
            Check(button.IsEnabled && calls == 1 && requestedId == "A" && ReferenceEquals(requestedPreset, source),
                "Single-display apply dispatches the selected stable ID and original preset despite duplicate names");
            // Reordering and renumbering cannot change the identity held by this selection.
            owner.Displays.Reverse(); owner.Displays.Single(d => d.Id == "A").Number = 9;
            Click(button);
            Check(calls == 2 && requestedId == "A", "Single-display picker keeps its target through display renumbering");
            picker.SelectedIndex = 0; Click(button);
            Check(calls == 3 && requestedId == "b", "Changing selection dispatches the newly chosen device identity");
            picker.SelectedIndex = -1;
            Check(!button.IsEnabled, "Clearing selection disables single-display apply");
            owner.Displays.Clear();
            editor = owner.CreateSceneDisplayEditor(source, Apply);
            Check(!Descendants<ComboBox>(editor).Single().IsEnabled && !Descendants<Button>(editor).Single().IsEnabled &&
                Descendants<TextBlock>(editor).Any(t => t.Text == L.T("scene.noMatchingDisplay")),
                "No connected saved displays gives an explanation and disables single-display apply");
            owner.Displays.Add(new() { Id = "A" }); source.Displays.Add(new() { Id = "a" });
            editor = owner.CreateSceneDisplayEditor(source, Apply);
            Check(Descendants<ComboBox>(editor).Single().Items.Count == 0,
                "Ambiguous saved device identities are excluded from single-display selection");
        }
        finally { owner.Displays = previous; }
    }
}
