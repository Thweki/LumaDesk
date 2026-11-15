// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Windows;

namespace LumaDesk;

internal static class QuickCompactLayout
{
    internal const string Flag = "LUMADESK_EXPERIMENTAL_TRAY_COMPACT";
    internal static bool IsEnabled => Environment.GetEnvironmentVariable(Flag) == "1";
    private const double RowHeight = 36;
    internal const double VisibleHeight = RowHeight * 3;
    internal static double Height(int count) => Math.Ceiling(count / 2.0) * RowHeight;

    internal static IReadOnlyList<QuickDisplayTile> Project(IReadOnlyList<DisplayInfo> displays, double width)
    {
        if (displays.Count == 0 || width <= 0) return Array.Empty<QuickDisplayTile>();
        var tiles = new List<QuickDisplayTile>();
        double columnWidth = width / 2, gap = Math.Min(4, columnWidth / 4);
        for (int i = 0; i < displays.Count; i++)
            tiles.Add(new(displays[i], new Rect(i % 2 * columnWidth, i / 2 * RowHeight,
                columnWidth - gap, RowHeight - 4)));
        return tiles;
    }
}
