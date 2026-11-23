// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class LayoutSwapTests
{
    internal static void Run(Action<bool, string> check)
    {
        var horizontal = new[]
        {
            new ScreenPlacement("A", -1920, -120, 1920, 1080, false),
            new ScreenPlacement("B", 0, 0, 2560, 1440, true),
            new ScreenPlacement("C", 2560, 0, 1280, 1024, false)
        };
        var before = horizontal.ToArray();
        var swapped = LayoutRules.SwapPositions(horizontal, "a", "b");
        check(swapped[0] == (horizontal[0] with { X = 2560, Y = 120 }) &&
            swapped[1] == horizontal[1] && swapped[2] == (horizontal[2] with { X = 4480, Y = 120 }),
            "Unequal horizontal neighbors exchange places and offsets with the same primary at zero");
        check(horizontal.SequenceEqual(before), "Swap does not mutate its input");
        check(LayoutRules.SwapPositions(horizontal, "B", "A").SequenceEqual(swapped), "Pair selection order does not affect horizontal swap");
        check(LayoutRules.SwapPositions(swapped, "A", "B").SequenceEqual(horizontal), "Swapping twice restores the original horizontal layout");

        var vertical = new[]
        {
            new ScreenPlacement("top", 0, -1920, 1080, 1920, false),
            new ScreenPlacement("bottom", 0, 0, 2560, 1440, true)
        };
        var stacked = LayoutRules.SwapPositions(vertical, "top", "bottom");
        check(stacked[0] == (vertical[0] with { Y = 1440 }) && stacked[1] == vertical[1],
            "Portrait and landscape vertical neighbors keep their sizes when swapped");
        check(LayoutRules.SwapPositions(vertical, "bottom", "top").SequenceEqual(stacked) &&
            LayoutRules.SwapPositions(stacked, "bottom", "top").SequenceEqual(vertical), "Vertical swap is symmetric and reversible");

        var row = new[]
        {
            new ScreenPlacement("left", -100, 0, 100, 100, false),
            new ScreenPlacement("middle", 0, 0, 100, 100, true),
            new ScreenPlacement("right", 100, 0, 100, 100, false)
        };
        var ends = LayoutRules.SwapPositions(row, "left", "right");
        check(ends[0].X == 100 && ends[2].X == -100 && ends[1] == row[1], "Nonadjacent displays exchange origins without moving the third display");
        var translated = horizontal.Select(p => p with { X = p.X + 20000, Y = p.Y - 10000 });
        check(LayoutRules.SwapPositions(translated, "A", "B").SequenceEqual(swapped), "Swap works with a translated preview origin");
        var extreme = new[]
        {
            new ScreenPlacement("a", int.MaxValue - 200, int.MinValue, 100, 100, true),
            new ScreenPlacement("b", int.MaxValue - 100, int.MinValue, 100, 100, false)
        };
        check(LayoutRules.SwapPositions(extreme, "a", "b")[1].X == -100, "Extreme absolute origins normalize before swap arithmetic");

        void Reject(ScreenPlacement[] source, string first, string second, string message)
        {
            var snapshot = source.ToArray(); bool rejected = false;
            try { LayoutRules.SwapPositions(source, first, second); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && source.SequenceEqual(snapshot), message + "; input remains intact");
        }
        Reject(horizontal, "A", "a", "Same display rejected case-insensitively");
        Reject(horizontal, "A", "missing", "Disconnected or unknown target rejected");
        Reject(Array.Empty<ScreenPlacement>(), "A", "B", "Empty desktop rejected");
        Reject(new[] { horizontal[1] }, "B", "A", "Single display rejected");
        Reject(new[] { row[0] with { Id = "MIDDLE" }, row[1] }, "middle", "left", "Duplicate identities rejected");
        Reject(new[] { row[1], row[2] with { X = 0 } }, "middle", "right", "Mirrored origins require extending first");
        Reject(new[] { row[1], row[2] with { Primary = true } }, "middle", "right", "Multiple primary displays rejected");
        Reject(new[] { row[1], row[2] with { Width = 0 } }, "middle", "right", "Invalid dimensions rejected");
        Reject(new[] { row[1], row[2] with { X = int.MaxValue } }, "middle", "right", "Out-of-range placement rejected without overflow");
        Reject(new[]
        {
            new ScreenPlacement("A", 0, 0, 100, 100, true),
            new ScreenPlacement("B", 100, 0, 100, 100, false),
            new ScreenPlacement("C", 200, 0, 200, 100, false)
        }, "A", "C", "Swap that would overlap a third display rejected");
        Reject(new[]
        {
            new ScreenPlacement("A", 0, 0, 100, 100, true),
            new ScreenPlacement("B", 100, 0, 200, 200, false),
            new ScreenPlacement("C", 250, 200, 50, 100, false)
        }, "A", "B", "Swap that would disconnect a third display rejected");
    }
}
