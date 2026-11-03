// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LumaDesk;

internal static class L
{
    internal static readonly string[] Languages = { "en", "zh-Hans", "zh-Hant", "ja", "ko" };
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> catalogs = new();
    private static readonly string systemLanguage = CultureInfo.CurrentUICulture.Name;
    internal static string Language { get; private set; } = Resolve(CultureInfo.CurrentUICulture.Name);
    internal static string Resolve(string? name)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(name ?? "");
            for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
            {
                if (current.Name.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase) || current.Name is "zh-TW" or "zh-HK" or "zh-MO") return "zh-Hant";
                if (current.Name.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
                if (current.Name == "zh") return "zh-Hans";
                if (current.Name is "ja" or "ko" or "en") return current.Name;
            }
        }
        catch (CultureNotFoundException) { }
        return "en";
    }
    internal static void Initialize(string? preference)
    {
        Language = Resolve(string.IsNullOrEmpty(preference) || preference == "auto" ? systemLanguage : preference);
        var culture = CultureInfo.GetCultureInfo(Language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // Keep CurrentCulture: Windows regional number/date formats are independent of UI language.
    }
    internal static IReadOnlyDictionary<string, string> Catalog(string language) => catalogs.GetOrAdd(language, name =>
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("LumaDesk.Localization." + name + ".json")
            ?? throw new InvalidOperationException("Missing localization resource: " + name);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Invalid language catalog: " + name);
    });
    internal static string T(string key)
    {
        if (Catalog(Language).TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return Catalog("en").TryGetValue(key, out value) ? value : key;
    }
    internal static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    internal static string NativeName(string language) => language switch
    {
        "zh-Hans" => "简体中文", "zh-Hant" => "繁體中文", "ja" => "日本語", "ko" => "한국어", _ => "English"
    };
}
