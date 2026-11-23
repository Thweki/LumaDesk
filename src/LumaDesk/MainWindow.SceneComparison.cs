// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal UIElement CreateSceneComparisonOption()
    {
        var toggle = Toggle(Preferences.ExperimentalPresetComparison, value => { Preferences.ExperimentalPresetComparison = value; Save(); });
        System.Windows.Automation.AutomationProperties.SetName(toggle, L.T("compare.option"));
        return Ui.Row(Ui.Text(L.T("compare.option")), toggle);
    }

    internal UIElement? CreateSceneComparison(Preset scene, Func<Task<Preset>>? capture = null)
    {
        if (!Preferences.ExperimentalPresetComparison) return null;
        capture ??= CaptureComparisonAsync;
        var content = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var results = new StackPanel();
        var compare = Ui.Button(L.T("compare.action"), () => { });
        bool reading = false;
        void Line(string text)
        {
            var block = Ui.Text(text, 12);
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 5, 0, 0);
            results.Children.Add(block);
        }
        compare.Click += async (_, _) =>
        {
            if (reading || !Preferences.ExperimentalPresetComparison) return;
            reading = true; compare.IsEnabled = false; results.Children.Clear(); Line(L.T("sd69db33602"));
            try
            {
                var current = await capture();
                results.Children.Clear();
                if (!Preferences.ExperimentalPresetComparison) return;
                var report = SceneComparison.Compare(scene, current);
                Line(L.T("compare.help"));
                if (scene.Displays.Count == 0) Line(L.T("compare.empty"));
                else if (report.Differences.Count == 0 && report.MissingDisplays.Count == 0 && report.ExtraDisplays.Count == 0)
                    Line(L.T("compare.match"));
                foreach (var name in report.MissingDisplays) Line(L.F("compare.missing", name));
                foreach (var name in report.ExtraDisplays) Line(L.F("compare.extra", name));
                foreach (var difference in report.Differences)
                    Line(L.F("compare.row", difference.DisplayName, L.T(difference.Setting), difference.Current, difference.Saved));
            }
            catch (Exception ex) { results.Children.Clear(); Line(L.T("compare.failed") + " " + ex.Message); }
            finally { reading = false; compare.IsEnabled = true; }
        };
        content.Children.Add(compare); content.Children.Add(results);
        return content;
    }

    private async Task<Preset> CaptureComparisonAsync()
    {
        if (sceneBusy || modePending || scanning || restoringConnection || Service.IsWriting) throw new InvalidOperationException(L.T("s92f637408f"));
        sceneBusy = true;
        var windows = System.Windows.Application.Current.Windows.Cast<Window>().Where(w => w.IsEnabled).ToArray();
        foreach (var window in windows) window.IsEnabled = false;
        try
        {
            // Scan into a separate snapshot: no refresh side effects, apply calls, save, or undo changes.
            var displays = await Service.ScanAsync();
            foreach (var display in displays)
            {
                display.Dimming = Displays.FirstOrDefault(d => string.Equals(d.Id, display.Id, StringComparison.OrdinalIgnoreCase))?.Dimming ?? 0;
                ApplyAlias(display);
            }
            return await SceneService.Capture(displays, d => Task.Run(() => AdvancedColorService.Read(d)), "");
        }
        finally
        {
            sceneBusy = false;
            foreach (var window in windows) window.IsEnabled = true;
        }
    }
}
