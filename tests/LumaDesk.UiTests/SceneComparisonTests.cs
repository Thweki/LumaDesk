// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static async Task TestSceneComparisonFixtureAsync(MainWindow owner)
    {
        bool previous = owner.Preferences.ExperimentalPresetComparison;
        var saved = new Preset { Displays = new() { new() { Id = "A", Brightness = 20 } } };
        var current = new Preset { Displays = new() { new() { Id = "A", Brightness = 70 } } };
        int reads = 0;
        var pending = new TaskCompletionSource<Preset>();
        Task<Preset> Capture() { reads++; return pending.Task; }
        try
        {
            owner.Preferences.ExperimentalPresetComparison = false;
            Check(owner.CreateSceneComparison(saved, Capture) == null && reads == 0,
                "Disabled experiment exposes no comparison controls and performs no reads");
            var toggle = Descendants<CheckBox>(owner.CreateSceneComparisonOption()).Single();
            toggle.IsChecked = true; toggle.RaiseEvent(new System.Windows.RoutedEventArgs(CheckBox.ClickEvent));
            Check(owner.Preferences.ExperimentalPresetComparison && Storage.Load().ExperimentalPresetComparison,
                "Application option persists the experimental comparison opt-in");
            var control = owner.CreateSceneComparison(saved, Capture)!;
            var button = Descendants<Button>(control).Single();
            Click(button); Click(button);
            Check(reads == 1 && !button.IsEnabled, "Comparison suppresses repeated clicks while reading");
            pending.SetResult(current);
            await Task.Yield();
            Check(button.IsEnabled && Descendants<TextBlock>(control).Any(t => t.Text.Contains("70%") && t.Text.Contains("20%")),
                "Comparison renders current and saved values without an apply action");
            pending = new TaskCompletionSource<Preset>();
            Click(button); pending.SetException(new InvalidOperationException("fixture failure"));
            await Task.Yield();
            Check(button.IsEnabled && Descendants<TextBlock>(control).Any(t => t.Text.Contains("fixture failure")) &&
                !Descendants<TextBlock>(control).Any(t => t.Text.Contains("70%")),
                "Failed refresh clears stale comparison results and allows retry");
            pending = new TaskCompletionSource<Preset>();
            Click(button); pending.SetResult(saved);
            await Task.Yield();
            Check(Descendants<TextBlock>(control).Any(t => t.Text == L.T("compare.match")),
                "A successful retry renders a matching snapshot");
            toggle.IsChecked = false; toggle.RaiseEvent(new System.Windows.RoutedEventArgs(CheckBox.ClickEvent));
            int before = reads; Click(button);
            Check(reads == before && !Storage.Load().ExperimentalPresetComparison && owner.CreateSceneComparison(saved, Capture) == null,
                "Disabling comparison persists and prevents retained controls from reading");
        }
        finally
        {
            owner.Preferences.ExperimentalPresetComparison = previous;
            Storage.Save(owner.Preferences);
        }
    }
}
