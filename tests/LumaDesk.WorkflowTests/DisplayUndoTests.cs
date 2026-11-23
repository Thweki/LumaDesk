// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class DisplayUndoTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var backend = new UndoBackend();
        var history = new DisplayUndoHistory();
        check(await history.UndoAsync(backend) == null && backend.Calls.Count == 0,
            "Empty undo history performs no hardware operations");
        var before = SceneService.CaptureOutput(backend.Displays);
        history.Remember(before, outputOnly: true);
        var a = backend.Screens[0];
        a.Current = new(1080, 1920, 75); a.Orientation = 1; a.DpiScale.CurrentPercent = 150;
        a.X = -1080; a.Primary = false;
        backend.Screens[1].X = 0; backend.Screens[1].Primary = true;
        a.Brightness = 17; a.Dimming = 23;
        check(before.Displays[0].Mode == new DisplayMode(1920, 1080, 60) &&
            before.Displays[0].Orientation == 0 && before.Displays[0].Dpi == 125 && before.Displays[0].Primary == true,
            "Output snapshot owns the old resolution, refresh rate, rotation, scaling and arrangement");
        backend.Screens.Reverse();
        backend.Screens.Single(d => d.Id == "A").Id = "a";
        var result = await history.UndoAsync(backend);
        a = backend.Screens.Single(d => d.Id == "a");
        check(result?.Issues.Count == 0 && history.Snapshot == null &&
            a.Current == new DisplayMode(1920, 1080, 60) && a.Orientation == 0 && a.DpiScale.CurrentPercent == 125 &&
            a.X == 0 && a.Primary && backend.Screens.Single(d => d.Id == "B").X == 1920,
            "Undo restores output and arrangement by stable identity after enumeration and casing changes");
        check(backend.Calls.SequenceEqual(new[] { "mode:a", "dpi:a", "layout" }) && backend.StaleReferences == 0,
            "Undo resolves replaced display objects, restores mode before scaling and applies layout last");
        check(a.Brightness == 17 && a.Dimming == 23,
            "Output undo preserves subsequent brightness and software dimming changes");
        backend.Calls.Clear();
        check(await history.UndoAsync(backend) == null && backend.Calls.Count == 0,
            "Successful undo consumes the slot without creating redo history");

        // Scaling undo is a delta: it also works on mirrored desktops and does not
        // undo mode changes or the scaling of a different screen.
        backend = new UndoBackend(); a = backend.Screens[0];
        backend.Screens[1].X = 0;
        history.Remember(SceneService.CaptureScale(a, 125), outputOnly: true);
        a.DpiScale.CurrentPercent = 150; a.Current = new(1280, 720, 60);
        backend.Screens[1].DpiScale.CurrentPercent = 200;
        result = await history.UndoAsync(backend);
        check(result?.Issues.Count == 0 && backend.Calls.SequenceEqual(new[] { "dpi:A" }) &&
            a.DpiScale.CurrentPercent == 125 && a.Current == new DisplayMode(1280, 720, 60) &&
            backend.Screens[1].DpiScale.CurrentPercent == 200,
            "Scaling undo changes only the selected display's scale, including mirrored setups");

        backend = new UndoBackend(); a = backend.Screens[0];
        before = SceneService.CaptureOutput(backend.Displays);
        history.Remember(before, outputOnly: true);
        var disconnected = backend.Screens[1]; backend.Screens.RemoveAt(1);
        result = await history.UndoAsync(backend);
        check(result!.Issues.Count > 0 && ReferenceEquals(history.Snapshot, before),
            "Disconnected monitors retain the original undo snapshot for retry");
        disconnected.X = 2000;
        backend.Screens.Add(disconnected); backend.FailLayout = true;
        result = await history.UndoAsync(backend);
        check(result!.Issues.Count > 0 && ReferenceEquals(history.Snapshot, before),
            "Failed layout restore retains the original undo snapshot");
        backend.FailLayout = false;
        result = await history.UndoAsync(backend);
        check(result!.Issues.Count == 0 && history.Snapshot == null && disconnected.X == 1920,
            "Retry completes the remaining restore and consumes history only on success");

        history.Remember(SceneService.CaptureOutput(backend.Displays), outputOnly: true);
        before = history.Snapshot!;
        a.Current = new(1280, 720, 60); backend.FailMode = true;
        result = await history.UndoAsync(backend);
        check(result!.Issues.Count == 2 && ReferenceEquals(history.Snapshot, before) && result.Applied == 1,
            "Failed output restore skips incompatible layout, still processes other displays and retains undo");
        backend.FailMode = false;
        result = await history.UndoAsync(backend);
        a = backend.Screens[0];
        check(result!.Issues.Count == 0 && history.Snapshot == null && a.Current == new DisplayMode(1920, 1080, 60),
            "Output undo retries the original mode after a driver failure");

        history.Remember(SceneService.CaptureScale(a, 100), outputOnly: true);
        before = history.Snapshot!;
        backend.FailDpi = true;
        result = await history.UndoAsync(backend);
        check(result!.Issues.Count == 1 && ReferenceEquals(history.Snapshot, before),
            "Failed scaling undo remains available for retry");
        backend.FailDpi = false;
        history.Remember(SceneService.CaptureScale(a, 150), outputOnly: true);
        await history.UndoAsync(backend);
        check(a.DpiScale.CurrentPercent == 150, "Only the most recent recorded adjustment is undone");

        // Full preset undo still restores brightness and color, while output undo
        // must never reach those backends even if a snapshot contains such fields.
        var preset = await SceneService.Capture(backend, "Before preset");
        history.Remember(preset);
        backend.Calls.Clear(); await history.UndoAsync(backend);
        check(backend.Calls.Contains("brightness:A") && backend.Calls.Contains("profiles:A") && !history.OutputOnly,
            "A preset replaces manual undo and retains full preset restoration");
        history.Remember(preset, outputOnly: true);
        backend.Calls.Clear(); await history.UndoAsync(backend);
        check(!backend.Calls.Any(c => c.StartsWith("brightness:") || c.StartsWith("profiles:") || c == "color" || c.StartsWith("hdr:")),
            "Manual output undo never queries or writes color or brightness");
    }

    private sealed class UndoBackend : ISceneBackend
    {
        internal List<DisplayInfo> Screens = new()
        {
            new() { Id = "A", Name = "A", Current = new(1920, 1080, 60), Primary = true,
                Brightness = 50, DpiScale = DpiRules.FromRelative(0, 1, 2) },
            new() { Id = "B", Name = "B", Current = new(1920, 1080, 60), X = 1920,
                Brightness = 70, DpiScale = DpiRules.FromRelative(0, 0, 4) }
        };
        public IReadOnlyList<DisplayInfo> Displays => Screens;
        internal List<string> Calls = new();
        internal bool FailDpi, FailLayout, FailMode;
        internal int StaleReferences;
        private void Call(string operation, DisplayInfo d)
        {
            Calls.Add(operation + ":" + d.Id);
            if (!Screens.Contains(d)) StaleReferences++;
        }
        public Task<DisplayColorState> Color(DisplayInfo d)
        {
            Calls.Add("color");
            return Task.FromResult(new DisplayColorState { HdrKnown = true, HdrSupported = true, ProfilesAvailable = true });
        }
        public Task Mode(DisplayInfo d, DisplayMode mode, uint orientation)
        {
            Call("mode", d);
            if (FailMode) throw new InvalidOperationException("Mode unavailable");
            Screens[Screens.IndexOf(d)] = new() { Id = d.Id, Name = d.Name, Current = mode, Orientation = orientation,
                DpiScale = d.DpiScale, X = d.X, Y = d.Y, Primary = d.Primary, Brightness = d.Brightness, Dimming = d.Dimming };
            return Task.CompletedTask;
        }
        public Task Dpi(DisplayInfo d, int percent)
        {
            Call("dpi", d);
            if (FailDpi) throw new InvalidOperationException("Scaling unavailable");
            d.DpiScale.CurrentPercent = percent; return Task.CompletedTask;
        }
        public Task Layout(IReadOnlyList<ScreenPlacement> placements)
        {
            Calls.Add("layout");
            if (FailLayout) throw new InvalidOperationException("Layout unavailable");
            foreach (var p in placements)
            {
                var d = Screens.Single(d => d.Id == p.Id);
                d.X = p.X; d.Y = p.Y; d.Primary = p.Primary;
            }
            return Task.CompletedTask;
        }
        public Task Hdr(DisplayInfo d, bool enabled) { Call("hdr", d); return Task.CompletedTask; }
        public Task Profiles(DisplayInfo d, PresetDisplay saved) { Call("profiles", d); return Task.CompletedTask; }
        public Task Brightness(DisplayInfo d, PresetDisplay saved)
        {
            Call("brightness", d); d.Brightness = saved.Brightness; d.Dimming = saved.Dimming; return Task.CompletedTask;
        }
    }
}
