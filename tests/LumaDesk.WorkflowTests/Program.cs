// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors
using System.Text.Json;
using System.IO;
using LumaDesk;

Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", Path.Combine(Path.GetTempPath(), "LumaDesk-workflow-" + Guid.NewGuid().ToString("N")));
L.Initialize("en");
if (args.Contains("--read-inputs"))
{
    var probe = new DisplayService();
    foreach (var display in await probe.ScanAsync())
    {
        try
        {
            var inputs = await probe.ReadInputsAsync(display);
            Console.WriteLine(JsonSerializer.Serialize(new { display.Name, display.Device, display.X, display.Y, inputs.Current, inputs.RawCurrent, inputs.LegacyCodes, Choices = inputs.Choices.Select(i => new { i.Code, i.TransportCode, Label = i.ToString() }) }));
        }
        catch (Exception ex) { Console.WriteLine(display.Name + ": " + ex.Message); }
    }
    return;
}
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var backend = new FakeBackend();
var scene = await SceneService.Capture(backend, "Work");
Check(scene.Displays.Count == 2 && scene.Displays[0].Dpi == 125 && scene.Displays[0].Hdr == false && scene.Displays[1].X == 1920, "Capture includes output, scale, HDR, ICC and arrangement");
var encoded = JsonSerializer.Serialize(scene, Storage.Json);
var decoded = JsonSerializer.Deserialize<Preset>(encoded, Storage.Json)!;
Check(decoded.Id == scene.Id && decoded.Displays[0].Mode == scene.Displays[0].Mode && decoded.Displays[0].SdrProfile == "sRGB.icm", "Full scene roundtrip retains identity, mode and profile");
var legacy = JsonSerializer.Deserialize<Preferences>("{\"Presets\":[{\"Name\":\"Night\",\"Displays\":[{\"Id\":\"A\",\"Brightness\":20,\"Dimming\":5}]}]}")!;
Check(legacy.Presets[0].Displays[0].Mode == null && legacy.Presets[0].Displays[0].Hdr == null && legacy.RestoreBrightness && legacy.HotkeyTarget == "pointer", "Legacy preferences keep new settings unspecified and useful defaults");
await SceneService.Apply(backend, legacy.Presets[0]);
Check(backend.Calls.SequenceEqual(new[] { "brightness:A" }), "Legacy brightness preset never writes mode, HDR, DPI or layout");
backend.Calls.Clear();
scene.Displays[0].Mode = new(1280, 720, 60); scene.Displays[0].Dpi = 150; scene.Displays[0].Hdr = true;
scene.Displays[1].X = 1280;
var applied = await SceneService.Apply(backend, scene);
Check(applied.Applied == 2 && applied.Issues.Count == 0, "A complete scene applies successfully");
Check(backend.Calls.IndexOf("mode:A") < backend.Calls.IndexOf("dpi:A") && backend.Calls.Last() == "layout", "Mode precedes scaling and arrangement is applied last");
Check(backend.StaleReferences == 0, "Scene resolves fresh monitor objects after topology-changing writes");
backend = new FakeBackend(); backend.FailBrightness.Add("A");
var partial = await SceneService.Apply(backend, await SceneService.Capture(backend, "Partial"));
Check(partial.Applied == 1 && partial.Issues.Any(i => i.Contains("A")) && backend.Calls.Contains("brightness:B"), "One failed monitor does not prevent another from applying");
backend = new FakeBackend(); scene = await SceneService.Capture(backend, "Missing"); backend.Screens.RemoveAt(1);
var missing = await SceneService.Apply(backend, scene);
Check(missing.Applied == 1 && missing.Issues.Count == 2 && !backend.Calls.Contains("layout"), "Missing monitor is skipped; partial desktop never gets a saved full layout");
backend = new FakeBackend(); scene = await SceneService.Capture(backend, "Extra"); backend.Screens.Add(new() { Id = "C" });
Check(SceneService.LayoutFor(scene, backend.Displays) == null && !SceneService.SameScreens(scene, backend.Displays), "Extra screens suppress saved layouts and exact-match automation");
backend = new FakeBackend(); scene = await SceneService.Capture(backend, "Overlap"); scene.Displays[1].X = 100;
Check(SceneService.LayoutFor(scene, backend.Displays) == null, "Overlapping saved layout is rejected before native calls");
scene.Displays[1].X = 1920; backend.Screens[0].Current = new(1280, 720, 60);
Check(SceneService.LayoutFor(scene, backend.Displays) == null, "Failed or changed output modes suppress incompatible arrangements");
backend = new FakeBackend(); scene = await SceneService.Capture(backend, "Duplicate"); scene.Displays.Add(scene.Displays[0]);
await SceneService.Apply(backend, scene);
Check(backend.Calls.Count(c => c == "brightness:A") == 1 && !backend.Calls.Contains("layout"), "Duplicate saved identities never produce duplicate writes or layouts");
backend = new FakeBackend { FailColor = true }; scene = await SceneService.Capture(backend, "No HDR");
Check(scene.Displays.All(d => d.Hdr == null && d.UserProfiles == null) && scene.Displays.All(d => d.Mode != null), "Unavailable color query does not prevent saving other capabilities");
Check(InputRules.Parse("(model(60(99))vcp(10 12 60(0F 10 11 12) D6(01 04)))").SequenceEqual(new uint[] { 15, 16, 17, 18 }), "Input parsing reads only reported VCP choices");
Check(InputRules.Parse("(vcp(10 60(11 11 12)))").SequenceEqual(new uint[] { 17, 18 }), "Repeated input choices are deduplicated");
var legacyInputs = InputRules.Resolve(7, new uint[] { 17, 18, 15 });
Check(legacyInputs.LegacyCodes && legacyInputs.Current == 15 && legacyInputs.Choices.Select(i => i.TransportCode).SequenceEqual(new uint[] { 5, 6, 7 }), "Observed nonstandard digital inputs map UI labels to the firmware's write codes");
Check(InputRules.Resolve(5, new uint[] { 17, 18, 15 }).Current == 17 && InputRules.Resolve(6, new uint[] { 17, 18, 15 }).Current == 18, "Compatibility readback normalizes HDMI inputs as well as DisplayPort");
Check(!InputRules.Resolve(17, new uint[] { 1, 3, 4, 15, 16, 17, 18 }).LegacyCodes && !InputRules.Resolve(7, new uint[] { 7, 15, 17 }).LegacyCodes, "Standard and analog-capable monitors never receive the digital compatibility mapping");
Check(!InputRules.Resolve(null, new uint[] { 15, 17 }).LegacyCodes && !InputRules.Resolve(99, new uint[] { 15, 17 }).LegacyCodes, "Missing or unrelated current values do not guess an input protocol");
Check(InputRules.Resolve(null, new uint[] { 15, 17 }, true).Choices.First().TransportCode == 7, "A known per-device compatibility mapping survives an unreadable current input");
int inputWrites = 0, inputReads = 0;
Task SendInput() { inputWrites++; return Task.CompletedTask; }
var verified = await InputSwitch.VerifyAsync(5, SendInput, () => Task.FromResult<uint?>(++inputReads == 1 ? 7u : 5u), () => Task.CompletedTask);
Check(verified.Confirmed && verified.Current == 5 && inputWrites == 1 && inputReads == 3, "Input switch writes once and requires stable target readback");
inputWrites = inputReads = 0;
bool unchanged = false;
try { await InputSwitch.VerifyAsync(5, SendInput, () => { inputReads++; return Task.FromResult<uint?>(7); }, () => Task.CompletedTask); }
catch (InvalidOperationException) { unchanged = true; }
Check(unchanged && inputWrites == 1 && inputReads == 5, "An acknowledged but ignored input write is reported as failure without blind retries");
inputWrites = 0;
var disconnectedInput = await InputSwitch.VerifyAsync(5, SendInput, () => Task.FromResult<uint?>(null), () => Task.CompletedTask);
Check(!disconnectedInput.Confirmed && inputWrites == 1, "Losing the input connection is unverified, never fabricated success or a repeated write");
inputReads = 0;
bool reverted = false;
try { await InputSwitch.VerifyAsync(5, SendInput, () => Task.FromResult<uint?>(++inputReads == 1 ? 5u : 7u), () => Task.CompletedTask); }
catch (InvalidOperationException) { reverted = true; }
Check(reverted, "A transient target followed by automatic fallback is not confirmed");
foreach (var bad in new[] { "(model(60(11)))", "(vcp(60(ZZ 11)))", "(vcp(60(100)))", "(vcp(60(00)))", "(vcp(60(11)", "(vcp(10 12))" })
    Check(InputRules.Parse(bad).Length == 0, "Malformed or absent input capabilities are not guessed: " + bad);
Check(GroupRules.Target(40, new() { Offset = 10 }, new() { Offset = -5 }) == 25, "Group offsets are relative to the screen being adjusted");
Check(GroupRules.Target(90, new(), new() { Maximum = 70 }) == 70 && GroupRules.Target(0, new(), new() { Minimum = 15 }) == 15, "Per-screen brightness boundaries apply without changing the source slider");
Check(GroupRules.Target(100, new(), new() { Minimum = 150, Maximum = -50 }) == 100, "Corrupt range cannot escape supported brightness bounds");
var day = new DateTime(2026, 11, 3);
Check(AutomationRules.CrossedTime("21:00", day.AddHours(20.99), day.AddHours(21.01)), "Schedule fires once when crossing its time");
Check(!AutomationRules.CrossedTime("21:00", day.AddHours(21.01), day.AddHours(21.02)), "Schedule does not repeat within the next polling interval");
Check(AutomationRules.CrossedTime("23:59", day.AddHours(23), day.AddDays(1).AddMinutes(1)), "Schedule handles midnight crossings");
Check(!AutomationRules.CrossedTime("25:00", day, day.AddDays(1)) && !AutomationRules.CrossedTime("12:00", day.AddDays(1), day), "Invalid time and backward clock changes do not trigger");
Check(CommandService.Parse(new[] { "--brightness", "A", "42", "--output", "result.json" }).Command == new ControlCommand("brightness", "A", 42), "CLI accepts an explicit bounded brightness target");
foreach (var invalid in new[] { new[] { "--brightness", "A", "101", "--output", "r.json" }, new[] { "--list" }, new[] { "--apply-preset", "--output", "r.json" }, new[] { "--list", "--output", "r.json", "extra" } })
{
    bool rejected = false; try { CommandService.Parse(invalid); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid CLI arguments are rejected before dispatch");
}
var service = new DisplayService();
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); bool stopped = false;
    try { await service.SetPercentAsync(new DisplayInfo { Id = "not-a-real-device" }, 0x10, 50, cancelled.Token); }
    catch (OperationCanceledException) { stopped = true; }
    Check(stopped && !service.IsWriting, "Cancelled reconnect write exits before device lookup and releases its busy state");
}
Console.WriteLine($"{checks} workflow checks passed. No native writes were issued.");

internal sealed class FakeBackend : ISceneBackend
{
    public List<DisplayInfo> Screens = new()
    {
        new() { Id = "A", Name = "A", Current = new(1920,1080,60), Primary = true, Brightness = 50, DpiScale = new() { CurrentPercent = 125, SupportedPercents = new[] { 100,125,150 }, UnavailableReason = "" } },
        new() { Id = "B", Name = "B", Current = new(1920,1080,60), X = 1920, Brightness = 50 }
    };
    public IReadOnlyList<DisplayInfo> Displays => Screens;
    public List<string> Calls { get; } = new();
    public HashSet<string> FailBrightness { get; } = new();
    public bool FailColor;
    public int StaleReferences;
    public Task<DisplayColorState> Color(DisplayInfo d) => FailColor ? Task.FromException<DisplayColorState>(new InvalidOperationException("No color API")) : Task.FromResult(new DisplayColorState { HdrKnown = true, HdrSupported = true, HdrUserEnabled = false, ProfilesAvailable = true, UseUserProfiles = true, SdrProfile = "sRGB.icm" });
    private void Call(string name, DisplayInfo d) { Calls.Add(name + ":" + d.Id); if (!Screens.Contains(d)) StaleReferences++; }
    public Task Mode(DisplayInfo d, DisplayMode mode, uint orientation)
    {
        Call("mode", d); Screens[Screens.IndexOf(d)] = new() { Id = d.Id, Name = d.Name, Current = mode, Orientation = orientation, X = d.X, Y = d.Y, Primary = d.Primary, Brightness = d.Brightness, DpiScale = d.DpiScale }; return Task.CompletedTask;
    }
    public Task Dpi(DisplayInfo d, int percent) { Call("dpi", d); d.DpiScale.CurrentPercent = percent; return Task.CompletedTask; }
    public Task Hdr(DisplayInfo d, bool enabled) { Call("hdr", d); return Task.CompletedTask; }
    public Task Profiles(DisplayInfo d, PresetDisplay saved) { Call("profiles", d); return Task.CompletedTask; }
    public Task Brightness(DisplayInfo d, PresetDisplay saved) { Call("brightness", d); if (FailBrightness.Contains(d.Id)) throw new InvalidOperationException("Disconnected"); return Task.CompletedTask; }
    public Task Layout(IReadOnlyList<ScreenPlacement> placements) { Calls.Add("layout"); return Task.CompletedTask; }
}
