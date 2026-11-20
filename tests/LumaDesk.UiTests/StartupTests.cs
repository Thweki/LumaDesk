// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Interop;

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

    private static void TestDeferredSettingsRenderingFixture()
    {
        var display = new DisplayInfo { Id = "deferred-fixture", Name = "Fixture Alpha", Number = 99 };
        var hiddenOwner = new MainWindow { Displays = new() { display } };
        Click(hiddenOwner.Navigation.Children.OfType<Button>().First());
        hiddenOwner.RefreshView();
        Check(hiddenOwner.PageContent.Children.Count == 0 && hiddenOwner.DisplayTabs.Children.Count == 0,
            "The default visibility guard leaves hidden settings controls unbuilt even with a populated model");

        bool visible = true;
        var owner = new MainWindow(() => visible) { Displays = new() { display } };
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        var about = navigation[^1];

        owner.RefreshView();
        Check(owner.PageContent.Children.Count > 0 && owner.DisplayTabs.Children.Count == 2,
            "Visible settings build the overview and its current display tab");
        var originalPage = owner.PageContent.Children[0];
        var originalTab = (Button)owner.DisplayTabs.Children[1];
        Check(Equals(originalTab.ToolTip, "Fixture Alpha"), "Visible settings render the current display name");

        visible = false;
        display.Name = "Fixture Beta";
        owner.RefreshView();
        Click(about);
        Check(ReferenceEquals(originalPage, owner.PageContent.Children[0])
            && ReferenceEquals(originalTab, owner.DisplayTabs.Children[1])
            && Equals(originalTab.ToolTip, "Fixture Alpha"),
            "Hidden refresh and navigation leave existing settings controls untouched");

        visible = true;
        owner.RefreshView();
        Check(!ReferenceEquals(originalPage, owner.PageContent.Children[0])
            && Equals(((Button)owner.DisplayTabs.Children[1]).ToolTip, "Fixture Beta")
            && owner.Breadcrumb.Text.EndsWith((string)about.Tag),
            "Visible rendering uses refreshed data and navigation selected while hidden");
        Check(new[] { hiddenOwner, owner }.All(w => !w.IsLoaded && new WindowInteropHelper(w).Handle == IntPtr.Zero),
            "Deferred-render owners stay detached and never load a desktop window");
    }
}
