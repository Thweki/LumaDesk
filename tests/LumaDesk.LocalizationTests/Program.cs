// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LumaDesk;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
foreach (var (input, expected) in new[] { ("zh-CN","zh-Hans"), ("zh-SG","zh-Hans"), ("zh-TW","zh-Hant"), ("zh-HK","zh-Hant"), ("zh-MO","zh-Hant"), ("zh-Hant","zh-Hant"), ("zh-Hans","zh-Hans"), ("en-GB","en"), ("ja-JP","ja"), ("ko-KR","ko"), ("fr-FR","en"), ("", "en"), ("bad/locale", "en") })
    Check(L.Resolve(input) == expected, "Windows locale mapping: " + input);
var source = L.Catalog("zh-Hans");
string Tokens(string value) => string.Join("|", Regex.Matches(value, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}").Select(m => m.Value).OrderBy(s => s));
foreach (string language in L.Languages)
{
    var catalog = L.Catalog(language);
    Check(catalog.Count == source.Count && source.Keys.All(catalog.ContainsKey), language + " contains every resource key");
    Check(catalog.All(p => !string.IsNullOrWhiteSpace(p.Value) && Tokens(p.Value) == Tokens(source[p.Key])), language + " preserves all format arguments");
    foreach (var value in catalog.Values)
    {
        var parsed = CompositeFormat.Parse(value);
        var formatArgs = Enumerable.Range(0, parsed.MinimumArgumentCount).Select(i => value.Contains("{" + i + ":HH:mm}") ? (object)new DateTime(2026,11,3,9,5,0) : 42).ToArray();
        _ = string.Format(CultureInfo.GetCultureInfo("de-DE"), parsed, formatArgs);
    }
    Check(true, language + " formats every translated template with Windows-style regional formatting");
}
Check(!L.Catalog("en").Values.Any(s => Regex.IsMatch(s, @"[\u4e00-\u9fff]")), "English fallback contains no untranslated Chinese");
var region = CultureInfo.GetCultureInfo("de-DE");
CultureInfo.CurrentCulture = region;
L.Initialize("ja-JP");
Check(L.Language == "ja" && CultureInfo.CurrentCulture.Name == "de-DE", "UI language does not overwrite Windows regional formats");
Check(await Task.Run(() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "ja", "Background tasks inherit UI language");
L.Initialize("fr-FR");
Check(L.Language == "en", "Unsupported language preference falls back to English");
Console.WriteLine($"{checks} localization checks passed; {source.Count} strings per language.");
