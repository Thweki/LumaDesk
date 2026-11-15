// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class AutoLayoutTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string flag = "LUMADESK_EXPERIMENTAL_AUTOLAYOUT";
        string? saved = Environment.GetEnvironmentVariable(flag);
        try
        {
            foreach (string? value in new string?[] { null, "0", "true", "yes", " 1", "1" })
            {
                Environment.SetEnvironmentVariable(flag, value);
                check(LayoutRules.AutoTidyEnabled == (value == "1"), "Auto tidy requires explicit opt-in: " + (value ?? "unset"));
            }
        }
        finally { Environment.SetEnvironmentVariable(flag, saved); }

        var primary = new ScreenPlacement("primary", 0, 0, 1920, 1080, true);
        var scattered = new[]
        {
            new ScreenPlacement("left", -2500, 0, 1280, 1024, false), primary,
            new ScreenPlacement("above", 0, -2000, 1600, 900, false),
            new ScreenPlacement("right", 3000, 0, 2560, 1440, false)
        };
        var before = scattered.ToArray();
        var tidy = LayoutRules.Tidy(scattered);
        check(tidy.SequenceEqual(new[] { scattered[0] with { X = -1280 }, primary,
            scattered[2] with { Y = -900 }, scattered[3] with { X = 1920 } }),
            "Tidy closes gaps while preserving left, above and right placement instead of flattening to a row");
        check(scattered.SequenceEqual(before), "Tidy never mutates the input preview");
        check(LayoutRules.Tidy(tidy).SequenceEqual(tidy), "Repeated tidy leaves a valid layout unchanged");
        check(LayoutRules.Tidy(scattered.Reverse()).OrderBy(p => p.Id).SequenceEqual(tidy.OrderBy(p => p.Id)),
            "Discovery order does not change tidy geometry");
        var stacked = scattered.Select(p => p with { X = 0, Y = 0 }).ToArray();
        check(LayoutRules.Tidy(stacked).OrderBy(p => p.Id).SequenceEqual(LayoutRules.Tidy(stacked.Reverse()).OrderBy(p => p.Id)),
            "Stable identity breaks distance ties for fully overlapping displays");
        var corner = new[] { primary, new ScreenPlacement("portrait", 1920, 1080, 1080, 1920, false) };
        check(LayoutRules.Tidy(corner)[1] == (corner[1] with { X = 840 }),
            "Corner-only contact becomes a traversable shared edge for a portrait display");
        check(LayoutRules.Tidy(new[] { primary with { X = int.MinValue, Y = int.MaxValue } }).Single() == primary,
            "Single display is anchored at zero even at extreme coordinates");

        var extreme = new[] { primary with { X = int.MinValue, Y = int.MaxValue },
            scattered[3] with { X = int.MaxValue, Y = int.MinValue } };
        check(LayoutRules.Normalize(LayoutRules.Tidy(extreme)).Count == 2, "Extreme relative coordinates do not overflow tidy scoring");

        var random = new Random(4127);
        for (int sample = 0; sample < 100; sample++)
        {
            var input = Enumerable.Range(0, 8).Select(i => new ScreenPlacement("screen-" + i,
                random.Next(-10000, 10001), random.Next(-10000, 10001), random.Next(600, 3841), random.Next(600, 2161), i == 3)).ToArray();
            if (sample == 0) input = input.Select(p => p with { X = 0, Y = 0 }).ToArray();
            var result = LayoutRules.Tidy(input);
            check(LayoutRules.Normalize(result).SequenceEqual(result) && result.Single(p => p.Primary).X == 0 && result.Single(p => p.Primary).Y == 0,
                "Tidy produces connected non-overlapping rectangles: " + sample);
            check(result.Select(p => (p.Id, p.Width, p.Height, p.Primary)).SequenceEqual(input.Select(p => (p.Id, p.Width, p.Height, p.Primary))),
                "Tidy preserves identity, order, dimensions and primary: " + sample);
            check(LayoutRules.Tidy(result).SequenceEqual(result), "Tidy is idempotent: " + sample);
        }

        foreach (var invalid in new[]
        {
            Array.Empty<ScreenPlacement>(), new[] { primary with { Primary = false } },
            new[] { primary, primary with { Id = "other" } },
            new[] { primary, primary with { Id = "PRIMARY", Primary = false } },
            new[] { primary with { Id = "" } }, new[] { primary with { Width = 0 } },
            new[] { primary with { Height = -1 } }, new[] { primary with { Width = int.MaxValue } },
            new[] { primary with { Width = 65535, Height = 65535 },
                primary with { Id = "other", Primary = false, Width = 65535, Height = 65535 } }
        })
        {
            bool rejected = false;
            try { LayoutRules.Tidy(invalid); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Invalid or unplaceable layout fails without returning a partial preview");
        }
    }
}
