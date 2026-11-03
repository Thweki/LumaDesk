// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Windows.Markup;
using System.Windows.Media;

namespace LumaDesk;

public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}

public sealed class UiFontExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new FontFamily(L.Language switch
    {
        "zh-Hans" => "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI",
        "zh-Hant" => "Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI",
        "ja" => "Segoe UI Variable Text, Segoe UI, Yu Gothic UI",
        "ko" => "Segoe UI Variable Text, Segoe UI, Malgun Gothic",
        _ => "Segoe UI Variable Text, Segoe UI"
    });
}

public sealed class UiLanguageExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => XmlLanguage.GetLanguage(L.Language);
}
