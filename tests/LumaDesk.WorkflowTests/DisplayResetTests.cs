// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class DisplayResetTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var resets = new DisplayResetService();
        var backend = new ResetBackend();
        var scene = new Preset { Name = "Work", Displays = new()
        {
            new() { Id = "A", Brightness = 35, Dimming = 15, Dpi = 150, Mode = new(1920, 1080, 60), Orientation = 0 },
            new() { Id = "B", Brightness = 60, Dpi = 125 }
        } };
        var all = DisplayResetFields.Brightness | DisplayResetFields.Scale | DisplayResetFields.Rotation;
        check(resets.Baseline("A") == null, "Reset starts without an invented factory or session baseline");
        bool rejected = false;
        try { await resets.Reset(backend, "A", all); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && backend.Calls.Count == 0, "Reset without an applied preset performs no writes");

        var applied = await SceneService.Apply(backend, scene);
        resets.Remember(scene, applied);
        check(applied.SuccessfulDisplayIds.SetEquals(new[] { "a", "b" }) && resets.Baseline("a")?.PresetName == "Work",
            "Successful preset application establishes baselines by case-insensitive device identity");
        scene.Name = "Edited"; scene.Displays[0].Brightness = 99; scene.Displays[0].Dpi = 100; scene.Displays.Clear();
        check(resets.Baseline("A") is { PresetName: "Work", Brightness: 35, Dpi: 150 },
            "Editing or deleting the source preset cannot mutate the applied baseline");

        backend.A.Brightness = 80; backend.A.Dimming = 40; backend.A.DpiScale.CurrentPercent = 100;
        backend.A.Current = new(1440, 2560, 75); backend.A.Orientation = 1; backend.Calls.Clear();
        var other = backend.Screens[1];
        var result = await resets.Reset(backend, "a", DisplayResetFields.Brightness);
        check(result.Applied == 1 && backend.Calls.SequenceEqual(new[] { "brightness:A" }) &&
            backend.A.Brightness == 35 && backend.A.Dimming == 15 && backend.A.DpiScale.CurrentPercent == 100 && backend.A.Orientation == 1,
            "Brightness reset restores the saved backlight/dimming pair without resetting scale or rotation");
        backend.Calls.Clear();
        await resets.Reset(backend, "A", DisplayResetFields.Scale);
        check(backend.Calls.SequenceEqual(new[] { "dpi:A" }) && backend.A.DpiScale.CurrentPercent == 150 && backend.A.Orientation == 1,
            "Scale-only reset writes only the saved DPI percentage");
        backend.Calls.Clear();
        await resets.Reset(backend, "A", DisplayResetFields.Rotation);
        check(backend.Calls.SequenceEqual(new[] { "mode:A" }) && backend.A.Current == new DisplayMode(2560, 1440, 75) && backend.A.Orientation == 0,
            "Rotation reset swaps the current dimensions and preserves refresh instead of restoring the preset's output mode");
        backend.A.Orientation = 2; backend.Calls.Clear();
        await resets.Reset(backend, "A", DisplayResetFields.Rotation);
        check(backend.A.Current == new DisplayMode(2560, 1440, 75) && backend.A.Orientation == 0,
            "A half-turn reset keeps width and height unchanged");

        backend.A.Orientation = 1; backend.A.Current = new(1440, 2560, 75);
        backend.A.Brightness = 90; backend.A.Dimming = 0; backend.A.DpiScale.CurrentPercent = 100; backend.Calls.Clear();
        result = await resets.Reset(backend, "A", all);
        check(result.Applied == 1 && backend.Calls.SequenceEqual(new[] { "mode:A", "dpi:A", "brightness:A" }) && backend.StaleReferences == 0,
            "Combined reset orders rotation before scale and brightness and resolves fresh objects after mode replacement");
        check(ReferenceEquals(other, backend.Screens[1]) && other.Brightness == 60 && other.DpiScale.CurrentPercent == 125 &&
            backend.A.X == 50 && backend.A.Y == 70 && backend.A.Primary,
            "Reset never targets the other screen or writes arrangement, primary, HDR or ICC");
        backend.Calls.Clear();
        await resets.Reset(backend, "A", all);
        check(backend.Calls.Count == 0, "Repeated reset skips controls already at the baseline");

        var replacement = new Preset { Name = "Evening", Displays = new()
        {
            new() { Id = "A", Brightness = 20, Dpi = 125 }, new() { Id = "B", Brightness = 25 }
        } };
        backend.FailBrightness = true;
        applied = await SceneService.Apply(backend, replacement); resets.Remember(replacement, applied);
        check(applied.SuccessfulDisplayIds.SetEquals(new[] { "B" }) && resets.Baseline("A")?.PresetName == "Work" &&
            resets.Baseline("B") is { PresetName: "Evening", Brightness: 25, Dpi: null },
            "A partially failed preset updates only successful displays and replaces rather than mixes baseline fields");
        backend.FailBrightness = false; backend.FailMode = true; backend.A.Orientation = 1;
        backend.A.Brightness = 90; backend.A.DpiScale.CurrentPercent = 100; backend.Calls.Clear();
        result = await resets.Reset(backend, "A", all);
        check(result.Applied == 0 && result.Issues.Count == 1 && backend.A.Orientation == 1 && backend.A.Brightness == 35 &&
            backend.A.DpiScale.CurrentPercent == 150 && resets.Baseline("A")?.PresetName == "Work",
            "Failed rotation reports partial reset while independent fields finish and the baseline remains retryable");
        backend.FailMode = false; backend.FailDpi = true; backend.A.DpiScale.CurrentPercent = 100; backend.A.Brightness = 90;
        result = await resets.Reset(backend, "A", all);
        check(result.Applied == 0 && result.Issues.Count == 1 && backend.A.Brightness == 35,
            "Unsupported scaling reports a failure without preventing brightness reset");
        backend.FailDpi = false; backend.FailBrightness = true; backend.A.Brightness = 90;
        result = await resets.Reset(backend, "A", DisplayResetFields.Brightness);
        check(result.Applied == 0 && result.Issues.Count == 1 && backend.A.Brightness == 90,
            "Brightness write failure never reports a completed reset");
        backend.FailBrightness = false;

        var legacy = new Preset { Name = "Old", Displays = new() { new() { Id = "A", Dimming = 200 } } };
        applied = await SceneService.Apply(backend, legacy); resets.Remember(legacy, applied);
        check(resets.Baseline("A")?.Available == DisplayResetFields.Brightness && resets.Baseline("A")?.Dimming == BrightnessRules.MaxDimming,
            "Legacy presets expose only brightness and retain the same clamped dimming target as application");
        backend.A.Brightness = 72; backend.A.Dimming = 0;
        await resets.Reset(backend, "A", DisplayResetFields.Brightness);
        check(backend.A.Brightness == 72 && backend.A.Dimming == 85, "Unspecified hardware brightness is preserved during software-only reset");
        foreach (var fields in new[] { DisplayResetFields.Scale, DisplayResetFields.Rotation, (DisplayResetFields)0, (DisplayResetFields)8 })
        {
            backend.Calls.Clear(); rejected = false;
            try { await resets.Reset(backend, "A", fields); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && backend.Calls.Count == 0, "Unavailable or invalid reset selection cannot write: " + fields);
        }
        backend.Screens.RemoveAt(0); backend.Calls.Clear();
        result = await resets.Reset(backend, "A", DisplayResetFields.Brightness);
        check(result.Applied == 0 && result.Issues.Count == 1 && backend.Calls.Count == 0,
            "Disconnected identity is reported without substituting another display");

        // Unplug during a topology-changing write, rather than just before reset.
        backend = new ResetBackend(); scene = new Preset { Displays = new() { new() { Id = "A", Mode = new(1920, 1080, 60), Orientation = 0, Dpi = 150, Brightness = 35 } } };
        resets.Remember(scene, await SceneService.Apply(backend, scene));
        backend.A.Orientation = 1; backend.DisconnectAfterMode = true; backend.Calls.Clear();
        result = await resets.Reset(backend, "A", all);
        check(result.Applied == 0 && result.Issues.Count == 2 && backend.Calls.SequenceEqual(new[] { "mode:A" }),
            "Hot-unplug between reset steps cannot write stale objects or redirect subsequent controls");
    }

    private sealed class ResetBackend : ISceneBackend
    {
        internal List<DisplayInfo> Screens = new()
        {
            new() { Id = "A", Name = "A", Current = new(1920, 1080, 60), Brightness = 50, X = 50, Y = 70, Primary = true },
            new() { Id = "B", Name = "B", Brightness = 50 }
        };
        internal DisplayInfo A => Screens[0];
        internal List<string> Calls = new();
        internal int StaleReferences;
        internal bool FailMode, FailDpi, FailBrightness, DisconnectAfterMode;
        public IReadOnlyList<DisplayInfo> Displays => Screens;
        private void Call(string operation, DisplayInfo d)
        {
            Calls.Add(operation + ":" + d.Id);
            if (!Screens.Contains(d)) StaleReferences++;
        }
        public Task Mode(DisplayInfo d, DisplayMode mode, uint orientation)
        {
            Call("mode", d);
            if (FailMode) throw new InvalidOperationException("Rejected mode");
            if (DisconnectAfterMode) Screens.Remove(d);
            else Screens[Screens.IndexOf(d)] = new() { Id = d.Id, Current = mode, Orientation = orientation,
                Brightness = d.Brightness, Dimming = d.Dimming, DpiScale = d.DpiScale, X = d.X, Y = d.Y, Primary = d.Primary };
            return Task.CompletedTask;
        }
        public Task Dpi(DisplayInfo d, int percent)
        {
            Call("dpi", d);
            if (FailDpi) throw new InvalidOperationException("Unsupported scale");
            d.DpiScale.CurrentPercent = percent; return Task.CompletedTask;
        }
        public Task Brightness(DisplayInfo d, PresetDisplay saved)
        {
            Call("brightness", d);
            if (FailBrightness && d.Id == "A") throw new InvalidOperationException("Failed DDC write");
            d.Brightness = saved.Brightness ?? d.Brightness;
            d.Dimming = Math.Clamp(saved.Dimming, 0, BrightnessRules.MaxDimming); return Task.CompletedTask;
        }
        public Task<DisplayColorState> Color(DisplayInfo d) => throw new Exception("Reset must not read color");
        public Task Hdr(DisplayInfo d, bool enabled) => throw new Exception("Reset must not write HDR");
        public Task Profiles(DisplayInfo d, PresetDisplay saved) => throw new Exception("Reset must not write ICC");
        public Task Contrast(DisplayInfo d, int percent) => throw new Exception("Reset must not write contrast");
        public Task Layout(IReadOnlyList<ScreenPlacement> placements) => throw new Exception("Reset must not write layout");
    }
}
