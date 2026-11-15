// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static async Task TestDisplayUndoFixtureAsync()
    {
        // Keep this owner unloaded so neither discovery nor native writes run.
        var owner = new MainWindow();
        var presets = owner.Navigation.Children.OfType<Button>().ElementAt(5);
        Click(presets);
        bool HasUndo(string key) => Descendants<Button>(owner.PageContent).Any(b => b.Content as string == L.T(key));
        Check(!HasUndo("scene.undo") && !HasUndo("scene.undoOutput"), "New session has no undo action");

        var display = new DisplayInfo { Id = "undo-fixture", Current = new(1920, 1080, 60) };
        var before = SceneService.CaptureScale(display, 125);
        owner.UndoHistory.Remember(before, outputOnly: true);
        Click(presets);
        Check(HasUndo("scene.undoOutput") && !HasUndo("scene.undo"),
            "Presets page names the manual display adjustment undo action");
        owner.Preferences.Presets.Clear();
        var quick = new QuickWindow(owner);
        try
        {
            quick.Render();
            var surface = (FrameworkElement)quick.Content;
            surface.Measure(new Size(360, 620)); surface.Arrange(new Rect(0, 0, 360, 620));
            Check(Descendants<Button>(surface).Any(b => b.Content as string == L.T("sf1de1e2621")),
                "Quick panel exposes undo through Presets even when no presets have been saved");
        }
        finally { quick.Close(); }

        // The early return must not replace a valid undo entry or query hardware.
        await owner.ChangeModeAsync(display, display.Current, display.Orientation);
        Check(ReferenceEquals(owner.UndoHistory.Snapshot, before) && owner.UndoHistory.OutputOnly,
            "Selecting the current output preserves the previous undo entry without hardware access");

        owner.UndoHistory.Remember(new Preset { Name = "Before preset" });
        Click(presets);
        Check(HasUndo("scene.undo") && !HasUndo("scene.undoOutput"),
            "A subsequent preset switch replaces the manual undo action in the shared slot");
        Check(!owner.IsLoaded && owner.Displays.Count == 0, "Undo UI fixture never starts display discovery");
    }
}
