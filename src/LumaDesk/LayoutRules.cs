using System;
using System.Collections.Generic;
using System.Linq;

namespace LumaDesk;

public sealed record ScreenPlacement(string Id, int X, int Y, int Width, int Height, bool Primary);
public static class LayoutRules
{
    public static List<ScreenPlacement> Normalize(IEnumerable<ScreenPlacement> screens)
    {
        var items = screens.ToList();
        if (items.Count == 0 || items.Count(i => i.Primary) != 1 || items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new InvalidOperationException("请选择且仅选择一台主显示器。");
        var primary = items.Single(i => i.Primary);
        var normalized = items.Select(i => i with { X = checked(i.X - primary.X), Y = checked(i.Y - primary.Y) }).ToList();
        if (normalized.Any(i => i.Width <= 0 || i.Height <= 0 || Math.Abs((long)i.X) + i.Width > 65535 || Math.Abs((long)i.Y) + i.Height > 65535))
            throw new InvalidOperationException("屏幕尺寸或位置超出可用范围。");
        for (int i = 0; i < normalized.Count; i++)
        for (int j = i + 1; j < normalized.Count; j++)
        {
            var a = normalized[i]; var b = normalized[j];
            if (Math.Min(a.X + a.Width, b.X + b.Width) > Math.Max(a.X, b.X) && Math.Min(a.Y + a.Height, b.Y + b.Height) > Math.Max(a.Y, b.Y))
                throw new InvalidOperationException("屏幕不能重叠。要显示相同内容，请使用复制模式。");
        }
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized[0].Id };
        bool added;
        do
        {
            added = false;
            foreach (var next in normalized.Where(i => !connected.Contains(i.Id)))
                if (normalized.Any(i => connected.Contains(i.Id) && Touch(i, next))) { connected.Add(next.Id); added = true; }
        } while (added);
        if (connected.Count != normalized.Count) throw new InvalidOperationException("请让屏幕边缘相接，避免鼠标无法跨屏。可使用横向排列或边缘吸附。");
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
