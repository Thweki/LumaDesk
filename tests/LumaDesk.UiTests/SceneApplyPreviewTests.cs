// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestSceneApplyPreviewFixture(MainWindow owner)
    {
        var preset = new Preset { Name = "Office", Displays = new()
        {
            new() { Id = "A", Name = "Saved screen", Brightness = 30 },
            new() { Id = "B", Name = "Missing screen" }
        } };
        var preview = new SceneApplyPreview(preset, new[] { new DisplayInfo { Id = "A", Name = "Desk", Number = 1 } });
        var dialog = owner.CreateSceneApplyPreviewDialog(preset.Name, preview);
        var buttons = Descendants<Button>((System.Windows.DependencyObject)dialog.Content).ToArray();
        Check(buttons.Single(b => b.IsCancel).IsDefault && buttons.Single(b => !b.IsCancel).IsEnabled,
            "Apply preview defaults to Cancel while offering explicit Apply for connected targets");
        var scroll = ((DockPanel)dialog.Content).Children.OfType<ScrollViewer>().Single();
        var text = Descendants<TextBlock>((StackPanel)scroll.Content).Select(t => t.Text).ToArray();
        Check(text.Contains("1 · Desk") && text.Contains("Missing screen") && text.Contains(L.T("scene.disconnected")),
            "Apply preview renders current identities and missing saved targets");
        dialog.Close();
        var empty = owner.CreateSceneApplyPreviewDialog(preset.Name, new SceneApplyPreview(preset, Array.Empty<DisplayInfo>()));
        Check(!Descendants<Button>((System.Windows.DependencyObject)empty.Content).Single(b => !b.IsCancel).IsEnabled,
            "Apply is disabled when no saved displays are connected");
        empty.Close();

        var previous = owner.Preferences.Presets;
        string? flag = Environment.GetEnvironmentVariable(SceneApplyPreview.Flag);
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        try
        {
            owner.Preferences.Presets = new() { preset };
            foreach (string? value in new string?[] { null, "1" })
            {
                Environment.SetEnvironmentVariable(SceneApplyPreview.Flag, value);
                Click(navigation[5]);
                string key = value == "1" ? "scene.previewApply" : "scene.apply";
                Check(Descendants<Button>(owner.PageContent).Any(b => b.Content as string == L.T(key)),
                    "Preset page advertises preview only when explicitly enabled: " + (value ?? "unset"));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(SceneApplyPreview.Flag, flag);
            owner.Preferences.Presets = previous;
            Click(navigation[0]);
        }
    }
}
