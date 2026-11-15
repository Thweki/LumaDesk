// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Globalization;
using System.Text.Json;
using LumaDesk;

internal static class SceneSearchTests
{
    internal static void Run(Action<bool, string> check)
    {
        var work = new Preset { Name = "Office Work", OnConnect = true, AtTime = "09:00" };
        var evening = new Preset { Name = "Evening", Displays = new() { new() { Name = "Office Work" } } };
        var remote = new Preset { Name = "Remote work" };
        var chinese = new Preset { Name = "办公显示器" };
        var literal = new Preset { Name = "Studio [HDR]*" };
        var presets = new List<Preset> { work, evening, remote, chinese, literal };
        string original = JsonSerializer.Serialize(presets, Storage.Json);
        Preset[] Search(string? query) => presets.Where(p => SceneService.MatchesName(p, query)).ToArray();

        foreach (string? query in new string?[] { null, "", " \t\r\n " })
            check(Search(query).SequenceEqual(presets), "Empty search returns all presets in saved order");
        check(Search("  wOrK \t").SequenceEqual(new[] { work, remote }),
            "Search matches name substrings ignoring case and surrounding whitespace, in saved order");
        check(Search("Office W").Single() == work && Search("Office  W").Length == 0,
            "Search preserves interior spaces and does not match saved display names");
        check(Search("显示").Single() == chinese, "Search supports non-Latin preset names");
        check(Search("[HDR]*").Single() == literal && Search(".*").Length == 0,
            "Search treats punctuation literally, without wildcard or regular expression expansion");
        check(Search(work.Id).Length == 0 && Search("09:00").Length == 0 && Search("missing").Length == 0,
            "Search matches only preset names, not IDs, automation, or unrelated text");
        check(!SceneService.MatchesName(new Preset { Name = "" }, "work"),
            "An empty preset name does not match a nonempty search");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            check(Search("OFFICE").Single() == work, "Name matching is independent of Windows regional casing rules");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        check(JsonSerializer.Serialize(presets, Storage.Json) == original,
            "Searching preserves preset order, identities, saved settings and automation");
    }
}
