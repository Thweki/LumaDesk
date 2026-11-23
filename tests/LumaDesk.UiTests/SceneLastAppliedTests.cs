// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Globalization;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestSceneLastAppliedFixture(MainWindow owner)
    {
        var previous = owner.Preferences.LastPresetApplication;
        var culture = CultureInfo.CurrentCulture;
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        try
        {
            owner.Preferences.LastPresetApplication = null;
            Click(navigation[5]);
            Check(Descendants<TextBlock>(owner.PageContent).Any(t => t.Text == L.T("scene.lastAppliedNone")),
                "Presets page explicitly shows that no successful application has been recorded");
            Check(((TextBlock)((MenuItem)owner.CreateSceneMenu().Items[0]).Header).Text == L.T("scene.lastAppliedNone"),
                "Tray preset menu shows the same empty history");

            var last = new PresetApplication
            {
                PresetId = "deleted-preset", PresetName = "Work _ HDR", AppliedAtUtc = new DateTimeOffset(2026, 11, 15, 14, 5, 6, TimeSpan.Zero)
            };
            owner.Preferences.LastPresetApplication = last;
            foreach (string region in new[] { "en-US", "de-DE", "ja-JP" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(region);
                string expected = L.F("scene.lastApplied", last.PresetName, last.AppliedAtUtc.ToLocalTime().ToString("G"));
                Click(navigation[0]); Click(navigation[5]);
                Check(Descendants<TextBlock>(owner.PageContent).Any(t => t.Text == expected) &&
                    Descendants<TextBlock>(owner.PageContent).Any(t => t.Text == L.T("scene.lastAppliedHelp")),
                    "Presets page shows historical name, local date/time and history explanation: " + region);
                var menu = owner.CreateSceneMenu();
                var summary = (MenuItem)menu.Items[0];
                Check(((TextBlock)summary.Header).Text == expected && AutomationProperties.GetName(summary) == expected &&
                    !summary.IsEnabled && !summary.IsChecked &&
                    (string)summary.ToolTip == L.T("scene.lastAppliedHelp"),
                    "Tray history is informational rather than an active-preset indicator: " + region);
                Check(menu.Items.OfType<MenuItem>().Skip(1).Select(i => i.Header as string)
                    .SequenceEqual(owner.Preferences.Presets.Select(p => p.Name).Append(L.T("scene.manage"))),
                    "History leaves preset actions in their existing order");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            owner.Preferences.LastPresetApplication = previous;
            Click(navigation[0]);
        }
    }
}
