// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text.Json;
using LumaDesk;

internal static class SceneGammaTests
{
    private static ushort[] Ramp(int shift = 0) => Enumerable.Range(0, 768)
        .Select(i => (ushort)Math.Min(65535, i % 256 * 256 + shift + i / 256)).ToArray();

    internal static async Task Run(Action<bool, string> check)
    {
        foreach (var value in new string?[] { null, "", "0", "true", "yes", " 1", "2" })
            check(!GammaService.IsEnabled(value), "Gamma opt-in stays off for: " + (value ?? "unset"));
        check(GammaService.IsEnabled("1"), "Gamma requires an explicit session opt-in");

        var backend = new GammaBackend();
        var disabled = await SceneService.Capture(backend, "Disabled");
        check(disabled.Displays.All(d => d.GammaRamp == null) && backend.GammaCalls.Count == 0,
            "Disabled capture never probes gamma");
        var preset = new Preset { Displays = new() { new() { Id = "A", GammaRamp = Ramp(10) } } };
        await SceneService.Apply(backend, preset);
        check(backend.GammaCalls.Count == 0 && backend.Operations.Writes == 0,
            "Disabled apply ignores even stored gamma ramps");

        backend.GammaEnabled = true;
        var captured = await SceneService.CaptureDraft(backend);
        check(captured.Displays.All(d => d.GammaRamp!.SequenceEqual(Ramp())) && backend.Operations.Writes == 0,
            "Enabled draft captures all three gamma channels without writes");
        backend.Operations.Current[200]++;
        check(captured.Displays[0].GammaRamp!.SequenceEqual(Ramp()), "Captured ramps are independent of live buffers");
        var copy = SceneService.Duplicate(captured, "Copy", Array.Empty<Preset>());
        copy.Displays[0].GammaRamp![100]++;
        check(captured.Displays[0].GammaRamp![100] == Ramp()[100], "Preset duplication deep-copies gamma ramps");
        var decoded = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(captured, Storage.Json), Storage.Json)!;
        check(decoded.Displays.All(d => d.GammaRamp!.SequenceEqual(Ramp())), "Gamma ramps survive JSON without channel or precision loss");
        var legacy = JsonSerializer.Deserialize<Preset>("{\"Displays\":[{\"Id\":\"A\"}]}")!;
        backend.GammaCalls.Clear();
        await SceneService.Apply(backend, legacy);
        check(legacy.Displays[0].GammaRamp == null && backend.GammaCalls.Count == 0,
            "Legacy presets never reset gamma to an invented default");

        backend = new GammaBackend { GammaEnabled = true };
        captured = await SceneService.Capture(backend, "Arrange");
        captured.Displays[0].Mode = new(1280, 720, 60);
        captured.Displays[1].X = 1280;
        captured.Displays[0].GammaRamp = Ramp(10);
        captured.Displays[1].GammaRamp = Ramp(20);
        backend.GammaCalls.Clear();
        var result = await SceneService.Apply(backend, captured);
        check(result.Applied == 2 && result.Issues.Count == 0 && backend.AfterLayout &&
            backend.GammaCalls.SequenceEqual(new[] { "layout", "write:A", "write:B" }) && backend.StaleReferences == 0,
            "Gamma restores after layout with freshly resolved display objects");
        check(backend.Written["A"].SequenceEqual(Ramp(10)) && backend.Written["B"].SequenceEqual(Ramp(20)),
            "Each stable display identity receives its own saved gamma ramp");

        backend = new GammaBackend { GammaEnabled = true, DisconnectOnLayout = true };
        result = await SceneService.Apply(backend, captured);
        check(result.Applied == 1 && result.Issues.Count == 1 && !backend.Written.ContainsKey("A") && backend.Written.ContainsKey("B"),
            "A display disappearing during layout is no longer counted as fully applied");

        backend = new GammaBackend { GammaEnabled = true };
        var hdrPreset = new Preset { Displays = new() { new() { Id = "A", Hdr = true, GammaRamp = Ramp(10) } } };
        result = await SceneService.Apply(backend, hdrPreset);
        check(result.Applied == 0 && result.Issues.Count == 1 && backend.Operations.Writes == 0,
            "Switching to HDR in the same preset prevents the final gamma write");

        backend = new GammaBackend { GammaEnabled = true };
        var before = await SceneService.Capture(backend, "Undo");
        await SceneService.Apply(backend, preset);
        await SceneService.Apply(backend, before);
        check(backend.Operations.Current.SequenceEqual(Ramp()) && backend.SecondOperations.Current.SequenceEqual(Ramp()),
            "Preset undo restores each captured gamma rather than a linear reset");

        backend = new GammaBackend { GammaEnabled = true, FailDisplay = "A" };
        captured = await SceneService.Capture(backend, "Unavailable");
        check(captured.Displays[0].GammaRamp == null && captured.Displays[1].GammaRamp != null && captured.Displays[0].Mode != null,
            "One unavailable gamma capture preserves other displays and preset settings");
        preset = new Preset { Displays = new() { new() { Id = "a", GammaRamp = Ramp(10) },
            new() { Id = "A", GammaRamp = Ramp(20) }, new() { Id = "B", GammaRamp = Ramp(30) } } };
        backend.GammaCalls.Clear();
        result = await SceneService.Apply(backend, preset);
        check(result.Applied == 1 && result.Issues.Count == 1 && result.Issues[0].Contains("Gamma") &&
            backend.GammaCalls.SequenceEqual(new[] { "write:A", "write:B" }),
            "A failed gamma write is reported once, deduplicates IDs and lets the next display apply");
        backend.Core.FailBrightness.Add("A");
        result = await SceneService.Apply(backend, preset);
        check(result.Applied == 1 && result.Issues.Count == 2,
            "Brightness and gamma failures on the same display do not decrement the applied count twice");
        backend.Core.Screens.RemoveAt(0); backend.GammaCalls.Clear();
        result = await SceneService.Apply(backend, preset);
        check(result.Applied == 1 && result.Issues.Count == 1 && backend.GammaCalls.SequenceEqual(new[] { "write:B" }),
            "Disconnected saved display is skipped without misdirecting its gamma ramp");

        var display = new DisplayInfo { Id = "A" };
        foreach (var state in new[] { new DisplayColorState(), new() { HdrKnown = true, ColorMode = "SDR", HdrActive = true },
            new() { HdrKnown = true, ColorMode = "SDR", HdrUserEnabled = true }, new() { HdrKnown = true, ColorMode = "WCG" } })
        {
            var ops = new GammaOperations { State = state };
            check(GammaService.Capture(display, ops) == null, "HDR or unknown color state leaves gamma unspecified");
            bool rejected = false;
            try { GammaService.Restore(display, Ramp(10), ops); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && ops.Reads == 0 && ops.Writes == 0, "HDR or unknown color state rejects gamma before native ramp access");
        }
        foreach (var failure in new[] { "ignored", "write", "readback" })
        {
            var ops = new GammaOperations { Failure = failure };
            bool rejected = false;
            try { GammaService.Restore(display, Ramp(10), ops); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && ops.Writes == 1, "Gamma failure is surfaced without blind retries: " + failure);
        }
        var same = new GammaOperations();
        GammaService.Restore(display, Ramp(), same);
        check(same.Writes == 0, "Matching gamma readback avoids a redundant native write");
        var descending = Ramp(); descending[100] = 0;
        foreach (var invalid in new[] { Array.Empty<ushort>(), new ushort[767], new ushort[769], new ushort[768], descending })
        {
            var ops = new GammaOperations(); bool rejected = false;
            try { GammaService.Restore(display, invalid, ops); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && ops.Reads == 0 && ops.Writes == 0, "Malformed, flat or descending gamma ramps never reach native code");
        }
        backend = new GammaBackend { GammaEnabled = true };
        preset.Displays[0].GammaRamp = new ushort[1];
        result = await SceneService.Apply(backend, preset);
        check(result.Applied == 1 && result.Issues.Count == 1 && backend.GammaCalls.SequenceEqual(new[] { "write:B" }),
            "Malformed persisted gamma reports a partial scene and preserves unrelated display writes");
    }

    private sealed class GammaOperations : IGammaOperations
    {
        internal DisplayColorState State = new() { HdrKnown = true, ColorMode = "SDR" };
        internal ushort[] Current = Ramp();
        internal int Reads, Writes;
        internal string Failure = "";
        public DisplayColorState Color(DisplayInfo display) => State;
        public ushort[] Read(DisplayInfo display)
        {
            Reads++;
            if (Failure == "readback" && Writes > 0) throw new InvalidOperationException("Readback unavailable");
            return Current;
        }
        public void Write(DisplayInfo display, ushort[] ramp)
        {
            Writes++;
            if (Failure == "write") throw new InvalidOperationException("Write failed");
            if (Failure != "ignored") Current = ramp.ToArray();
        }
    }

    private sealed class GammaBackend : ISceneBackend, ISceneGammaBackend
    {
        internal FakeBackend Core = new();
        internal GammaOperations Operations = new();
        internal GammaOperations SecondOperations = new();
        internal List<string> GammaCalls = new();
        internal Dictionary<string, ushort[]> Written = new();
        internal string? FailDisplay;
        internal bool AfterLayout;
        internal bool DisconnectOnLayout;
        internal int StaleReferences;
        public bool GammaEnabled { get; set; }
        public IReadOnlyList<DisplayInfo> Displays => Core.Displays;
        public Task<DisplayColorState> Color(DisplayInfo d) => Core.Color(d);
        public Task Mode(DisplayInfo d, DisplayMode mode, uint orientation) => Core.Mode(d, mode, orientation);
        public Task Dpi(DisplayInfo d, int percent) => Core.Dpi(d, percent);
        public async Task Hdr(DisplayInfo d, bool enabled)
        {
            await Core.Hdr(d, enabled);
            For(d).State.HdrUserEnabled = enabled;
            For(d).State.HdrActive = enabled;
        }
        public Task Profiles(DisplayInfo d, PresetDisplay saved) => Core.Profiles(d, saved);
        public Task Brightness(DisplayInfo d, PresetDisplay saved) => Core.Brightness(d, saved);
        public async Task Layout(IReadOnlyList<ScreenPlacement> placements)
        {
            await Core.Layout(placements);
            GammaCalls.Add("layout");
            Core.Screens = Core.Screens.Select(d => new DisplayInfo { Id = d.Id, Name = d.Name }).ToList();
            if (DisconnectOnLayout) Core.Screens.RemoveAt(0);
            AfterLayout = true;
        }
        private GammaOperations For(DisplayInfo d) => d.Id == "A" ? Operations : SecondOperations;
        public Task<ushort[]?> ReadGamma(DisplayInfo d)
        {
            GammaCalls.Add("read:" + d.Id);
            if (d.Id == FailDisplay) throw new InvalidOperationException("Unsupported display");
            return Task.FromResult(GammaService.Capture(d, For(d)));
        }
        public Task RestoreGamma(DisplayInfo d, ushort[] ramp)
        {
            GammaCalls.Add("write:" + d.Id);
            if (!Core.Screens.Contains(d)) StaleReferences++;
            if (Core.Calls.Contains("layout") && !AfterLayout) throw new Exception("Gamma must follow layout");
            if (d.Id == FailDisplay) throw new InvalidOperationException("Unsupported display");
            GammaService.Restore(d, ramp, For(d));
            Written[d.Id] = ramp.ToArray();
            return Task.CompletedTask;
        }
    }
}
