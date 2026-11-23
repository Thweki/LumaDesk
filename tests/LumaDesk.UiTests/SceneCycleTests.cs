// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LumaDesk;
using Forms = System.Windows.Forms;

internal static partial class Program
{
    private sealed class SceneCycleMenu : Forms.ContextMenuStrip
    {
        internal void RefreshItems() => OnOpening(new CancelEventArgs());
    }

    private static async Task TestSceneCycleFixtureAsync(MainWindow owner)
    {
        var previous = owner.Preferences.Presets;
        var first = new Preset { Name = "Work & play" };
        var second = new Preset { Name = "Evening" };
        var applied = new List<Preset>();
        string settings = Path.Combine(Storage.Folder, "settings.json");
        string original = File.ReadAllText(settings);
        using var menu = new SceneCycleMenu();
        try
        {
            owner.Preferences.Presets = new();
            owner.AddSceneCycleTrayItems(menu, p => { applied.Add(p); return Task.CompletedTask; });
            var items = menu.Items.OfType<Forms.ToolStripMenuItem>().ToArray();
            Check(items.Length == 2 && items.All(i => !i.Enabled),
                "Tray exposes two disabled cycle actions when no presets are saved");
            owner.Preferences.Presets.AddRange(new[] { first, second });
            menu.RefreshItems();
            Check(items.All(i => i.Enabled) && items[0].Text == L.T("scene.previousPreset") + ": Evening" &&
                items[1].Text == L.T("scene.nextPreset") + ": Work && play" &&
                items[1].AccessibleName == L.T("scene.nextPreset") + ": Work & play",
                "Tray actions show localized directions and target names with literal ampersands");
            items[1].PerformClick(); items[0].PerformClick();
            Check(applied.SequenceEqual(new[] { first, second }), "Both tray actions dispatch exactly one target");

            // Change order after opening to exercise click-time resolution, not cached targets.
            owner.Preferences.Presets.Reverse();
            items[1].PerformClick();
            Check(applied.Count == 3 && applied[2] == second, "Tray resolves current saved order when clicked");
            owner.Preferences.Presets.Clear();
            items[0].PerformClick();
            Check(applied.Count == 3, "A stale menu cannot apply a removed preset");
            menu.RefreshItems();
            Check(items.All(i => !i.Enabled), "Reopening refreshes empty-list availability");

            owner.Preferences.Presets.Add(first);
            menu.RefreshItems();
            var pending = new TaskCompletionSource<Preset>();
            var capture = owner.CaptureSceneDraftAsync(() => pending.Task);
            try
            {
                items[1].PerformClick();
                Check(applied.Count == 3, "Click-time busy guard blocks stale enabled actions during capture");
                menu.RefreshItems();
                Check(items.All(i => !i.Enabled), "Opening the tray during scene work disables cycle actions");
            }
            finally
            {
                pending.SetException(new InvalidOperationException("fixture capture cancelled"));
                try { await capture; } catch (InvalidOperationException) { }
            }
            menu.RefreshItems();
            items[0].PerformClick();
            Check(items.All(i => i.Enabled) && applied.Count == 4 && applied[3] == first,
                "Cycle actions recover after failure and allow reapplying a single saved preset");
            Check(File.ReadAllText(settings) == original, "Menu navigation does not persist a cursor or change settings");
        }
        finally { owner.Preferences.Presets = previous; }
    }
}
