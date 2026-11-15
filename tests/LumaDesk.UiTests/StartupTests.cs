// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.Linq;
using System.Windows.Controls;

internal static partial class Program
{
    private static void TestDeferredSettingsRendering(MainWindow window)
    {
        Check(window.PageContent.Children.Count == 0 && window.DisplayTabs.Children.Count == 0,
            "Tray startup scans displays without building settings controls");
        window.ShowSettings();
        Check(window.PageContent.Children.Count > 0 && window.DisplayTabs.Children.Count == window.Displays.Count + 1,
            "Opening settings builds the overview and current display tabs");

        var navigation = window.Navigation.Children.OfType<Button>().ToArray();
        var overview = window.PageContent.Children[0];
        window.ShowSettings();
        Check(ReferenceEquals(overview, window.PageContent.Children[0]),
            "Activating visible settings preserves the current controls");
        window.Hide();
        Click(navigation[6]); // A render request while hidden must preserve the existing controls.
        Click(navigation[0]);
        Check(ReferenceEquals(overview, window.PageContent.Children[0]),
            "Hidden render requests do not rebuild the settings page");

        var display = window.Displays[0];
        string originalName = display.Name;
        try
        {
            display.Name = "Deferred settings display";
            window.RefreshView();
            Check(ReferenceEquals(overview, window.PageContent.Children[0]),
                "Background view refresh leaves hidden settings controls untouched");
            window.ShowSettings();
            Check(Descendants<TextBlock>(window.DisplayTabs).Any(t => t.Text == display.Name),
                "Reopening settings renders the latest display data");
            Click(navigation[6]);
            Check(window.Breadcrumb.Text.EndsWith((string)navigation[6].Tag),
                "Visible navigation still renders immediately");
            window.Hide();
            Click(navigation[0]);
            window.ShowSettings();
            Check(window.Breadcrumb.Text.EndsWith((string)navigation[0].Tag),
                "Reopening settings honors navigation requested while hidden");
        }
        finally
        {
            display.Name = originalName;
            Click(navigation[0]);
            window.ShowSettings();
        }
    }
}
