// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace LumaDesk;

public sealed record ScreenPlacement(string Id, int X, int Y, int Width, int Height, bool Primary);
public static class LayoutRules
{
    public static List<ScreenPlacement> SwapPositions(IEnumerable<ScreenPlacement> screens, string firstId, string secondId)
    {
        var items = Normalize(screens);
        var first = items.FirstOrDefault(p => string.Equals(p.Id, firstId, StringComparison.OrdinalIgnoreCase));
        var second = items.FirstOrDefault(p => string.Equals(p.Id, secondId, StringComparison.OrdinalIgnoreCase));
        if (first == null || second == null || first == second)
            throw new InvalidOperationException(L.T("layout.swap.choose"));

        var movedFirst = first with { X = second.X, Y = second.Y };
        var movedSecond = second with { X = first.X, Y = first.Y };
        // Unequal neighbors must exchange places around their shared edge,
        // keeping the pair's outer edges, rather than exchanging origins alone.
        if (Touch(first, second) && first.X + first.Width == second.X)
            movedFirst = movedFirst with { X = second.X + second.Width - first.Width };
        else if (Touch(first, second) && second.X + second.Width == first.X)
            movedSecond = movedSecond with { X = first.X + first.Width - second.Width };
        else if (Touch(first, second) && first.Y + first.Height == second.Y)
            movedFirst = movedFirst with { Y = second.Y + second.Height - first.Height };
        else if (Touch(first, second) && second.Y + second.Height == first.Y)
            movedSecond = movedSecond with { Y = first.Y + first.Height - second.Height };

        // Validate the whole desktop before publishing any preview change. Other
        // screens retain their positions apart from primary-origin translation.
        return Normalize(items.Select(p => p == first ? movedFirst : p == second ? movedSecond : p));
    }
    public static List<ScreenPlacement> Normalize(IEnumerable<ScreenPlacement> screens)
    {
        var items = screens.ToList();
        if (items.Count == 0 || items.Count(i => i.Primary) != 1 || items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new InvalidOperationException(L.T("sc918c29047"));
        var primary = items.Single(i => i.Primary);
        var normalized = items.Select(i =>
        {
            // Validate in a wider type so extreme saved coordinates follow the
            // same rejection path as other unsupported layouts, without overflow.
            long x = (long)i.X - primary.X, y = (long)i.Y - primary.Y;
            if (i.Width <= 0 || i.Height <= 0 || Math.Abs(x) + i.Width > 65535 || Math.Abs(y) + i.Height > 65535)
                throw new InvalidOperationException(L.T("sa9fa1bf809"));
            return i with { X = (int)x, Y = (int)y };
        }).ToList();
        for (int i = 0; i < normalized.Count; i++)
        for (int j = i + 1; j < normalized.Count; j++)
        {
            var a = normalized[i]; var b = normalized[j];
            if (Math.Min(a.X + a.Width, b.X + b.Width) > Math.Max(a.X, b.X) && Math.Min(a.Y + a.Height, b.Y + b.Height) > Math.Max(a.Y, b.Y))
                throw new InvalidOperationException(L.T("s0947ffa0b1"));
        }
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized[0].Id };
        bool added;
        do
        {
            added = false;
            foreach (var next in normalized.Where(i => !connected.Contains(i.Id)))
                if (normalized.Any(i => connected.Contains(i.Id) && Touch(i, next))) { connected.Add(next.Id); added = true; }
        } while (added);
        if (connected.Count != normalized.Count) throw new InvalidOperationException(L.T("s559b035be9"));
        return normalized;
    }
    private static bool Touch(ScreenPlacement a, ScreenPlacement b) =>
        ((a.X + a.Width == b.X || b.X + b.Width == a.X) && Math.Min(a.Y + a.Height, b.Y + b.Height) > Math.Max(a.Y, b.Y)) ||
        ((a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && Math.Min(a.X + a.Width, b.X + b.Width) > Math.Max(a.X, b.X));
    public static ScreenPlacement Snap(ScreenPlacement moving, IEnumerable<ScreenPlacement> others, int distance)
    {
        int x = moving.X, y = moving.Y, dx = distance + 1, dy = distance + 1;
        foreach (var other in others.Where(i => i.Id != moving.Id))
        {
            foreach (int candidate in new[] { other.X - moving.Width, other.X + other.Width, other.X, other.X + other.Width - moving.Width })
                if (Math.Abs((long)candidate - moving.X) < dx) { x = candidate; dx = Math.Abs(candidate - moving.X); }
            foreach (int candidate in new[] { other.Y - moving.Height, other.Y + other.Height, other.Y, other.Y + other.Height - moving.Height })
                if (Math.Abs((long)candidate - moving.Y) < dy) { y = candidate; dy = Math.Abs(candidate - moving.Y); }
        }
        return moving with { X = x, Y = y };
    }
}
