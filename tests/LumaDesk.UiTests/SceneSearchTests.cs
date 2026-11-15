// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LumaDesk;

internal static partial class Program
{
    private static void TestSceneSearchFixture(MainWindow owner)
    {
        var work = new Preset { Name = "Office Work", OnConnect = true, AtTime = "09:00" };
        var evening = new Preset { Name = "Evening" };
        var remote = new Preset { Name = "Remote work" };
        var previous = owner.Preferences.Presets;
        var navigation = owner.Navigation.Children.OfType<Button>().ToArray();
        TextBox Search() => Descendants<TextBox>(owner.PageContent).Single(t => AutomationProperties.GetName(t) == L.T("scene.search"));
        Button Clear() => Descendants<Button>(owner.PageContent).Single(b => b.Content as string == L.T("scene.clearSearch"));
        Border[] Cards() => owner.PageContent.Children.OfType<Border>()
            .Where(c => Descendants<Button>(c).Any(b => b.Content as string == L.T("panel.up"))).ToArray();
        Border NoMatches() => owner.PageContent.Children.OfType<Border>()
            .Single(c => Descendants<TextBlock>(c).Any(t => t.Text == L.T("scene.noSearchResults")));
        bool CountIs(int count, int total) => Descendants<TextBlock>(owner.PageContent)
            .Any(t => t.Text == L.F("scene.searchCount", count, total));
        try
        {
            owner.Preferences.Presets = new() { work, evening, remote };
            Storage.Save(owner.Preferences);
            string original = JsonSerializer.Serialize(owner.Preferences, Storage.Json);
            string settingsPath = Path.Combine(Storage.Folder, "settings.json");
            byte[] saved = File.ReadAllBytes(settingsPath);
            Click(navigation[5]);
            var cards = Cards();
            var search = Search();
            Check(cards.Length == 3 && cards.All(c => c.Visibility == Visibility.Visible) &&
                CountIs(3, 3) && !Clear().IsEnabled && NoMatches().Visibility == Visibility.Collapsed,
                "Preset search starts empty with all cards in saved order and no empty-result message");
            var details = Descendants<Expander>(cards[0]).Single(e => e.Header as string == L.T("scene.more"));
            details.IsExpanded = true;
            var edit = Descendants<TextBox>((DependencyObject)details.Content).Single(t => t.Text == work.Name);
            edit.Text = "Unsubmitted edit";
            search.Text = "  WoRK  ";
            Check(Cards().Where(c => c.Visibility == Visibility.Visible).SequenceEqual(new[] { cards[0], cards[2] }) &&
                CountIs(2, 3) && Clear().IsEnabled && Search() == search,
                "Typing filters matching cards live in saved order without replacing the search control");
            Check(cards.All(c => ((StackPanel)Descendants<Button>(c).Single(b => b.Content as string == L.T("panel.up")).Parent)
                .Visibility == Visibility.Collapsed), "Search hides reorder controls to avoid moving across hidden neighbors");
            search.Text = "no such preset";
            Check(cards.All(c => c.Visibility == Visibility.Collapsed) && CountIs(0, 3) && NoMatches().Visibility == Visibility.Visible,
                "An unmatched search has an explicit empty-result message and zero count");
            Click(Clear());
            Check(search.Text == "" && Cards().SequenceEqual(cards) && cards.All(c => c.Visibility == Visibility.Visible) &&
                edit.Text == "Unsubmitted edit" && details.IsExpanded && NoMatches().Visibility == Visibility.Collapsed,
                "Clearing restores the same cards, including expanded details and unsaved edits");
            Check(cards.All(c => ((StackPanel)Descendants<Button>(c).Single(b => b.Content as string == L.T("panel.up")).Parent)
                .Visibility == Visibility.Visible), "Clearing restores reorder controls");
            search.Text = " \t ";
            Check(cards.All(c => c.Visibility == Visibility.Visible) && CountIs(3, 3),
                "Whitespace-only search shows every preset");
            Check(JsonSerializer.Serialize(owner.Preferences, Storage.Json) == original && File.ReadAllBytes(settingsPath).SequenceEqual(saved),
                "Searching and clearing neither mutate preferences nor write settings");

            search.Text = "evening";
            Click(navigation[0]); Click(navigation[5]);
            Check(Search().Text == "evening" && CountIs(1, 3), "Search survives page navigation and rerendering in this session");
            var visible = Cards().Single(c => c.Visibility == Visibility.Visible);
            var visibleDetails = Descendants<Expander>(visible).Single(e => e.Header as string == L.T("scene.more"));
            Click(Descendants<Button>((DependencyObject)visibleDetails.Content).Single(b => b.Content as string == L.T("s2f9daa8289")));
            Check(owner.Preferences.Presets.SequenceEqual(new[] { work, remote }) && Search().Text == "evening" &&
                CountIs(0, 2) && NoMatches().Visibility == Visibility.Visible &&
                Storage.Load().Presets.Select(p => p.Id).SequenceEqual(new[] { work.Id, remote.Id }),
                "Filtered card actions target the saved preset and refresh the results after deletion");
            Click(Clear());
            Check(Cards().All(c => c.Visibility == Visibility.Visible) && CountIs(2, 2), "Clear restores remaining presets after deletion");
            owner.Preferences.Presets.Clear(); Click(navigation[5]);
            Search().Text = "work";
            Check(Cards().Length == 0 && CountIs(0, 0) && NoMatches().Visibility == Visibility.Collapsed &&
                Descendants<TextBlock>(owner.PageContent).Any(t => t.Text == L.T("scene.empty")),
                "An empty library retains its save-first guidance rather than showing no search matches");
        }
        finally
        {
            Click(Clear());
            owner.Preferences.Presets = previous;
            Storage.Save(owner.Preferences);
            Click(navigation[0]);
        }
    }
}
