// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

internal static partial class Program
{
    private static void TestDeferredSettingsRendering(MainWindow window)
    {
        Check(window.PageContent.Children.Count == 0 && window.DisplayTabs.Children.Count == 0,
            "Tray startup scans displays without building settings controls");
        window.ShowSettings();
        Check(window.PageContent.Children.Count > 0 && window.TabStrip.Visibility == Visibility.Collapsed,
            "Opening settings builds the overview without duplicating the display picker");

        var navigation = window.Navigation.Children.OfType<Button>().ToArray();
        var overview = window.PageContent.Children[0];
        window.ShowSettings();
        Check(ReferenceEquals(overview, window.PageContent.Children[0]),
            "Activating visible settings preserves the current controls");
        window.Hide();
        Click(navigation[^1]); // A render request while hidden must preserve the existing controls.
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
            Check(Descendants<Button>(window.PageContent).Any(b => AutomationProperties.GetName(b) == L.T("s84041df0ce") + display.Name),
                "Reopening settings renders the latest display data");
            Click(navigation[^1]);
            Check(window.Breadcrumb.Text.EndsWith((string)navigation[^1].Tag),
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
        var application = navigation.Single(b => Equals(b.Tag, L.T("s63c73c4730")));
        Check(navigation.Length == 6 && navigation.All(b => !Equals(b.Tag, "关于")), "Settings omits the About page");

        owner.RefreshView();
        Button DisplayRow() => Descendants<Button>(owner.PageContent).Single(b => AutomationProperties.GetName(b) == L.T("s84041df0ce") + display.Name);
        Check(owner.PageContent.Children.Count > 0 && owner.TabStrip.Visibility == Visibility.Collapsed,
            "Overview lists displays once");
        Click(DisplayRow());
        Check(owner.TabStrip.Visibility == Visibility.Visible && owner.DisplayTabs.Children.Count == 2,
            "Opening a display reveals navigation back to the overview and between screens");
        var originalPage = owner.PageContent.Children[0];
        var originalTab = (Button)owner.DisplayTabs.Children[1];
        Check(Equals(originalTab.ToolTip, "Fixture Alpha"), "Visible settings render the current display name");

        visible = false;
        display.Name = "Fixture Beta";
        owner.RefreshView();
        Click(application);
        Check(ReferenceEquals(originalPage, owner.PageContent.Children[0])
            && ReferenceEquals(originalTab, owner.DisplayTabs.Children[1])
            && Equals(originalTab.ToolTip, "Fixture Alpha"),
            "Hidden refresh and navigation leave existing settings controls untouched");

        visible = true;
        owner.RefreshView();
        Check(!ReferenceEquals(originalPage, owner.PageContent.Children[0])
            && owner.DisplayTabs.Children.Count == 0 && owner.TabStrip.Visibility == Visibility.Collapsed
            && owner.Breadcrumb.Text.EndsWith((string)application.Tag),
            "Visible rendering uses refreshed data and navigation selected while hidden");
        Check(Descendants<CheckBox>(owner.PageContent).Any(c => AutomationProperties.GetName(c) == L.T("sde38b92ad2")),
            "Application settings exposes start at sign-in");
        CheckBox UpdateToggle() => Descendants<CheckBox>(owner.PageContent).Single(c => AutomationProperties.GetName(c) == L.T("updates.title"));
        Check(UpdateToggle().IsChecked == false, "Automatic updates default off");
        foreach (bool enabled in new[] { true, false })
        {
            var toggle = UpdateToggle();
            toggle.IsChecked = enabled;
            toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(Storage.Load().AutoUpdate == enabled && UpdateToggle().IsChecked == enabled, "Automatic updates preference persists");
        }
        Click(navigation[0]);
        Click(DisplayRow());
        Check(Equals(((Button)owner.DisplayTabs.Children[1]).ToolTip, "Fixture Beta"), "Reopening the display uses its new name");
        Check(new[] { hiddenOwner, owner }.All(w => !w.IsLoaded && new WindowInteropHelper(w).Handle == IntPtr.Zero),
            "Deferred-render owners stay detached and never load a desktop window");
    }
}
