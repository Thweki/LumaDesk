// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static async Task TestSceneGammaFixtureAsync(MainWindow owner)
    {
        const string flag = "LUMADESK_EXPERIMENTAL_GAMMA";
        string? previousFlag = Environment.GetEnvironmentVariable(flag);
        var previous = owner.Preferences.Presets;
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        // Read collapsed Expander content without showing/templating a desktop window.
        IEnumerable<T> Nodes<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            {
                if (child is T match) yield return match;
                foreach (var descendant in Nodes<T>(child)) yield return descendant;
            }
        }
        bool Has(string key) => Nodes<TextBlock>(owner.PageContent).Any(t => t.Text == L.T(key));
        void Render() { Click(navigation[0]); Click(navigation[5]); }
        try
        {
            owner.Preferences.Presets = new() { new() { Name = "Gamma", Displays = new()
            {
                new() { Id = "A", Name = "Captured display", GammaRamp = Enumerable.Range(0, 768).Select(i => (ushort)(i % 256 * 257)).ToArray() },
                new() { Id = "B", Name = "Unavailable display" }
            } } };
            Environment.SetEnvironmentVariable(flag, null); Render();
            Check(!GammaService.Enabled && !Has("gamma.help") && !Has("gamma.saved"),
                "Experimental gamma UI is absent by default even with saved ramps");
            Environment.SetEnvironmentVariable(flag, "1"); Render();
            Check(GammaService.Enabled && Has("gamma.help") && Has("gamma.saved") && Has("gamma.absent"),
                "Opt-in presets explain gamma and distinguish captured from unavailable ramps");
            await owner.CaptureSceneDraftAsync(() => Task.FromResult(
                SceneService.Duplicate(owner.Preferences.Presets[0], "Gamma draft", Array.Empty<Preset>())));
            Check(Nodes<TextBlock>(owner.PageContent).Count(t => t.Text == L.T("gamma.saved")) == 2 &&
                Nodes<TextBlock>(owner.PageContent).Count(t => t.Text == L.T("gamma.absent")) == 2,
                "Unsaved draft exposes gamma capture availability without native calls");
            Click(Nodes<Button>(owner.PageContent).Single(b => b.Content as string == L.T("scene.discardDraft")));
            Environment.SetEnvironmentVariable(flag, null); Render();
            Check(!Has("gamma.help") && owner.Preferences.Presets[0].Displays[0].GammaRamp != null,
                "Disabling gamma hides its UI without deleting existing preset ramps");
        }
        finally
        {
            var discard = Nodes<Button>(owner.PageContent).FirstOrDefault(b => b.Content as string == L.T("scene.discardDraft"));
            if (discard != null) Click(discard);
            Environment.SetEnvironmentVariable(flag, previousFlag);
            owner.Preferences.Presets = previous;
            Click(navigation[0]);
        }
    }
}
