// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class LayoutConfigurationTests
{
    internal static void Run(Action<bool, string> check)
    {
        DpiNative.Path Path(uint id, uint index) => new() { Source = new() { Id = id, ModeIndex = index } };
        SavedLayoutScreen Screen(string device, int x, int y) => new() { Device = device, Mode = new() { X = x, Y = y } };
        LayoutService.ConfigMode[] Modes() => new[]
        {
            new LayoutService.ConfigMode { Type = 1, X = 100, Y = 200, Data5 = 123 },
            new LayoutService.ConfigMode { Type = 2, Data0 = 456, Data5 = 789 },
            new LayoutService.ConfigMode { Type = 1, X = 300, Y = 400, Data5 = 321 }
        };
        var paths = new[] { Path(10, 2), Path(20, 0) };
        var screens = new[] { Screen("B", 0, 0), Screen("A", -1920, 120) };
        var modes = Modes();
        var reads = new List<uint>();
        string? Resolve(DpiNative.Path path)
        {
            reads.Add(path.Source.Id);
            return path.Source.Id == 10 ? "A" : "B";
        }
        LayoutService.UpdatePositions(screens, paths, modes, Resolve);
        check(reads.SequenceEqual(new uint[] { 10, 20 }), "Layout resolves each path once, independent of screen order");
        check(modes[0].X == 0 && modes[0].Y == 0 && modes[2].X == -1920 && modes[2].Y == 120,
            "Layout maps device names to source mode indices, not path or screen positions");
        check(modes[0].Data5 == 123 && modes[2].Data5 == 321 && modes[1].Equals(Modes()[1]),
            "Layout preserves target timing and non-position source data");

        reads.Clear();
        LayoutService.UpdatePositions(screens, paths, modes, path =>
        {
            reads.Add(path.Source.Id);
            return path.Source.Id == 10 ? "B" : "A";
        });
        check(reads.Count == 2 && modes[0].X == -1920 && modes[2].X == 0,
            "A new layout configuration resolves fresh source names after topology changes");

        void Reject(string message, DpiNative.Path[] input, LayoutService.ConfigMode[] configuration, Func<DpiNative.Path, string?> resolve)
        {
            bool rejected = false;
            try { LayoutService.UpdatePositions(screens, input, configuration, resolve); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, message);
        }
        Reject("Layout rejects duplicate source names instead of picking one", paths, Modes(), _ => "B");
        Reject("Layout rejects a failed source-name query", paths, Modes(), p => p.Source.Id == 10 ? "A" : null);
        Reject("Layout rejects missing devices", paths, Modes(), _ => "unrelated");
        Reject("Layout preserves ordinal device matching", paths, Modes(), p => p.Source.Id == 10 ? "a" : "b");
        Reject("Layout rejects out-of-range source mode indices", new[] { Path(10, 3), Path(20, 0) }, Modes(), Resolve);
        Reject("Layout rejects target modes used as source modes", new[] { Path(10, 1), Path(20, 0) }, Modes(), Resolve);
        Reject("Layout rejects shared source mode indices", new[] { Path(10, 0), Path(20, 0) }, Modes(), Resolve);
        Reject("Layout rejects extra active paths", new[] { Path(10, 2), Path(20, 0), Path(30, 1) }, Modes(),
            p => p.Source.Id == 30 ? "C" : Resolve(p));

        foreach (int count in new[] { 1, 4, 16 })
        {
            var manyPaths = Enumerable.Range(0, count).Select(i => Path((uint)i, (uint)i)).ToArray();
            var manyScreens = Enumerable.Range(0, count).Select(i => Screen(i.ToString(), i * 100, 0)).ToArray();
            var manyModes = Enumerable.Range(0, count).Select(_ => new LayoutService.ConfigMode { Type = 1 }).ToArray();
            int calls = 0;
            LayoutService.UpdatePositions(manyScreens, manyPaths, manyModes, p => { calls++; return p.Source.Id.ToString(); });
            check(calls == count, $"Layout uses {count} source-name queries for {count} paths, not a query per screen/path pair");
        }
    }
}
