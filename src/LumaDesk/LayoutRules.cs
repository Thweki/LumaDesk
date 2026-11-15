// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace LumaDesk;

public sealed record ScreenPlacement(string Id, int X, int Y, int Width, int Height, bool Primary);
public static class LayoutRules
{
    internal static bool AutoTidyEnabled => Environment.GetEnvironmentVariable("LUMADESK_EXPERIMENTAL_AUTOLAYOUT") == "1";

    public static List<ScreenPlacement> Tidy(IEnumerable<ScreenPlacement> screens)
    {
        var items = screens.ToList();
        if (items.Count == 0 || items.Count(i => i.Primary) != 1 ||
            items.Any(i => string.IsNullOrWhiteSpace(i.Id)) ||
            items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new InvalidOperationException(L.T("sc918c29047"));
        if (items.Any(i => i.Width <= 0 || i.Height <= 0 || i.Width > 65535 || i.Height > 65535))
            throw new InvalidOperationException(L.T("sa9fa1bf809"));
        // Already connected, non-overlapping layouts need no tidying (also makes repeat clicks stable).
        try { return Normalize(items); }
        catch (InvalidOperationException) { }

        var primary = items.Single(i => i.Primary);
        var placed = new List<ScreenPlacement> { primary with { X = 0, Y = 0 } };
        // Place nearby screens first; identity breaks ties independently of discovery order.
        foreach (var screen in items.Where(i => !i.Primary)
            .OrderBy(i => Math.Abs((long)i.X - primary.X) + Math.Abs((long)i.Y - primary.Y))
            .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase))
        {
            long desiredX = (long)screen.X - primary.X, desiredY = (long)screen.Y - primary.Y;
            var candidates = new List<ScreenPlacement>();
            foreach (var anchor in placed)
            {
                // Edge alignments plus the nearest position with full overlap of the smaller edge.
                int overlapY = Math.Min(anchor.Height, screen.Height), overlapX = Math.Min(anchor.Width, screen.Width);
                foreach (int y in new[] { anchor.Y, anchor.Y + anchor.Height - screen.Height,
                    (int)Math.Clamp(desiredY, anchor.Y - screen.Height + overlapY, anchor.Y + anchor.Height - overlapY) })
                {
                    candidates.Add(screen with { X = anchor.X - screen.Width, Y = y });
                    candidates.Add(screen with { X = anchor.X + anchor.Width, Y = y });
                }
                foreach (int x in new[] { anchor.X, anchor.X + anchor.Width - screen.Width,
                    (int)Math.Clamp(desiredX, anchor.X - screen.Width + overlapX, anchor.X + anchor.Width - overlapX) })
                {
                    candidates.Add(screen with { X = x, Y = anchor.Y - screen.Height });
                    candidates.Add(screen with { X = x, Y = anchor.Y + anchor.Height });
                }
            }
            var next = candidates.Where(c => Math.Abs((long)c.X) + c.Width <= 65535 && Math.Abs((long)c.Y) + c.Height <= 65535 &&
                    placed.All(p => Math.Min(c.X + c.Width, p.X + p.Width) <= Math.Max(c.X, p.X) ||
                                    Math.Min(c.Y + c.Height, p.Y + p.Height) <= Math.Max(c.Y, p.Y)))
                .OrderBy(c => Math.Abs((long)c.X - desiredX) + Math.Abs((long)c.Y - desiredY))
                .ThenBy(c => c.X).ThenBy(c => c.Y).FirstOrDefault();
            if (next == null) throw new InvalidOperationException(L.T("layout.tidy.unavailable"));
            placed.Add(next);
        }
        // Preserve UI order and run the same validation as manual placement before publishing a preview.
        return Normalize(items.Select(i => placed.Single(p => p.Id == i.Id)));
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
