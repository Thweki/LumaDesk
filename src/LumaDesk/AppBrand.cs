// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

namespace LumaDesk;

internal static class AppBrand
{
    public const string Name = "LumaDesk";
    public static string Version => typeof(AppBrand).Assembly.GetName().Version!.ToString(3);
}
