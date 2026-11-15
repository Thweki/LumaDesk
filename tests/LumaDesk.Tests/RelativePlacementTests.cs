// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class RelativePlacementTests
{
    internal static void Run(Action<bool, string> check)
    {
        var anchor = new ScreenPlacement("anchor", -2560, -400, 2560, 1440, true);
        var portrait = new ScreenPlacement("portrait", 9000, 8000, 1080, 1920, false);
        var cases = new[]
        {
            (LayoutSide.Left, LayoutAlignment.Start, -3640, -400),
            (LayoutSide.Left, LayoutAlignment.Center, -3640, -640),
            (LayoutSide.Left, LayoutAlignment.End, -3640, -880),
            (LayoutSide.Right, LayoutAlignment.Start, 0, -400),
            (LayoutSide.Right, LayoutAlignment.Center, 0, -640),
            (LayoutSide.Right, LayoutAlignment.End, 0, -880),
            (LayoutSide.Above, LayoutAlignment.Start, -2560, -2320),
            (LayoutSide.Above, LayoutAlignment.Center, -1820, -2320),
            (LayoutSide.Above, LayoutAlignment.End, -1080, -2320),
            (LayoutSide.Below, LayoutAlignment.Start, -2560, 1040),
            (LayoutSide.Below, LayoutAlignment.Center, -1820, 1040),
            (LayoutSide.Below, LayoutAlignment.End, -1080, 1040)
        };
        foreach (var (side, alignment, x, y) in cases)
        {
            var placed = LayoutRules.PlaceRelative(portrait, anchor, side, alignment);
            check(placed == (portrait with { X = x, Y = y }), $"Relative {side}/{alignment} places portrait display exactly and preserves metadata");
            check(LayoutRules.Normalize(new[] { anchor, placed }).Count == 2, $"Relative {side}/{alignment} leaves a connected, nonoverlapping pair");
            check(LayoutRules.PlaceRelative(placed, anchor, side, alignment) == placed, $"Relative {side}/{alignment} is stable when repeated");
        }
        check(portrait.X == 9000 && anchor.X == -2560, "Relative placement never mutates its input records");
        foreach (int height in new[] { 1079, 1921 })
        {
            var placed = LayoutRules.PlaceRelative(portrait with { Height = height, Primary = true }, anchor, LayoutSide.Right, LayoutAlignment.Center);
            check(placed.Y == (height == 1079 ? -220 : -641) && placed.Primary,
                "Odd positive/negative center offsets round toward top and retain primary flag: " + height);
        }
        var wide = LayoutRules.PlaceRelative(portrait with { Width = 2561 }, anchor, LayoutSide.Above, LayoutAlignment.Center);
        check(wide.X == -2561, "Odd horizontal center offsets round toward left");
        var primary = LayoutRules.PlaceRelative(anchor, portrait, LayoutSide.Left, LayoutAlignment.End);
        var normalized = LayoutRules.Normalize(new[] { primary, portrait });
        check(normalized[0].Primary && normalized[0].X == 0 && normalized[0].Y == 0 && normalized[1].X == 2560 && normalized[1].Y == -480,
            "Moving primary still normalizes the complete desktop on apply");

        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            check(rejected, message);
        }
        Reject(() => LayoutRules.PlaceRelative(portrait, portrait with { Id = "PORTRAIT" }, LayoutSide.Right, LayoutAlignment.Center), "Same device rejected case-insensitively");
        Reject(() => LayoutRules.PlaceRelative(portrait with { Width = 0 }, anchor, LayoutSide.Left, LayoutAlignment.Start), "Invalid moving dimensions rejected");
        Reject(() => LayoutRules.PlaceRelative(portrait, anchor with { Height = -1 }, LayoutSide.Above, LayoutAlignment.Start), "Invalid anchor dimensions rejected");
        Reject(() => LayoutRules.PlaceRelative(portrait, anchor, (LayoutSide)99, LayoutAlignment.Center), "Invalid side rejected");
        Reject(() => LayoutRules.PlaceRelative(portrait, anchor, LayoutSide.Right, (LayoutAlignment)99), "Invalid alignment rejected");
        foreach (var (side, x, y) in new[] { (LayoutSide.Right, int.MaxValue, 0), (LayoutSide.Left, int.MinValue, 0), (LayoutSide.Above, 0, int.MinValue), (LayoutSide.Below, 0, int.MaxValue) })
            Reject(() => LayoutRules.PlaceRelative(portrait, anchor with { X = x, Y = y }, side, LayoutAlignment.Start), "Coordinate overflow rejected: " + side);

        var placedRight = LayoutRules.PlaceRelative(portrait, anchor, LayoutSide.Right, LayoutAlignment.Center);
        var third = placedRight with { Id = "third" };
        Reject(() => LayoutRules.Normalize(new[] { anchor, placedRight, third }), "Apply still rejects a relative placement that overlaps a third screen");
        Reject(() => LayoutRules.Normalize(new[] { anchor, placedRight, third with { X = 20000 } }), "Apply still rejects a disconnected third screen");
    }
}
