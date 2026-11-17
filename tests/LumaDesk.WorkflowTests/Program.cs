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
backend = new FakeBackend(); scene = await SceneService.Capture(backend, "Extreme coordinates");
scene.Displays[0].X = int.MinValue; scene.Displays[1].X = int.MaxValue;
var invalidLayout = await SceneService.Apply(backend, scene);
Check(invalidLayout.Applied == 2 && invalidLayout.Issues.SequenceEqual(new[] { L.T("scene.layoutSkipped") }) &&
    backend.Calls.Contains("brightness:A") && backend.Calls.Contains("brightness:B") && !backend.Calls.Contains("layout"),
    "Extreme saved coordinates skip native layout writes and preserve the scene's partial result");
backend = new FakeBackend(); scene = await SceneService.Capture(backend, "Duplicate"); scene.Displays.Add(scene.Displays[0]);
await SceneService.Apply(backend, scene);
Check(backend.Calls.Count(c => c == "brightness:A") == 1 && !backend.Calls.Contains("layout"), "Duplicate saved identities never produce duplicate writes or layouts");
backend = new FakeBackend { FailColor = true }; scene = await SceneService.Capture(backend, "No HDR");
Check(scene.Displays.All(d => d.Hdr == null && d.UserProfiles == null) && scene.Displays.All(d => d.Mode != null), "Unavailable color query does not prevent saving other capabilities");
Check(scene.Displays.All(d => d.SdrProfile == null && d.HdrProfile == null), "Unavailable ICC values remain unspecified, not captured absence");

FakeBackend ProfileBackend(FakeProfileOperations profiles)
{
    var result = new FakeBackend { ProfileOperations = profiles };
    result.Screens.RemoveAt(1);
    return result;
}
var profiles = new FakeProfileOperations();
profiles.Hdr.Add("HDR.icc");
backend = ProfileBackend(profiles);
scene = await SceneService.Capture(backend, "Absent SDR");
scene = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(scene, Storage.Json), Storage.Json)!;
Check(scene.Displays[0].UserProfiles == true && scene.Displays[0].SdrProfile == "" && scene.Displays[0].HdrProfile == "HDR.icc", "Capture and persistence retain an absent SDR default alongside a named HDR default");
profiles.Sdr.AddRange(new[] { "assigned.icc", "fallback.icc" });
applied = await SceneService.Apply(backend, scene);
Check(applied.Applied == 1 && applied.Issues.Count == 0 && profiles.Sdr.Count == 0 && profiles.Hdr.Single() == "HDR.icc", "Apply restores captured SDR absence and preserves the HDR default");
Check(profiles.Calls.SequenceEqual(new[] { "remove:SDR:assigned.icc", "remove:SDR:fallback.icc" }), "Absence uses native association removal and follows promoted defaults only in the saved subtype");
profiles.Calls.Clear();
await SceneService.Apply(backend, scene);
Check(profiles.Calls.Count == 0, "Reapplying matching empty and named defaults issues no writes");

profiles = new FakeProfileOperations(); backend = ProfileBackend(profiles);
scene = await SceneService.Capture(backend, "Empty user scope");
profiles.User = false;
profiles.Sdr.Add("dormant-SDR.icc"); profiles.Hdr.Add("dormant-HDR.icc");
applied = await SceneService.Apply(backend, scene);
Check(applied.Issues.Count == 0 && profiles.User && profiles.Sdr.Count == 0 && profiles.Hdr.Count == 0, "Both empty defaults restore the saved user scope from system scope");
Check(profiles.Calls.SequenceEqual(new[] { "scope:True", "remove:SDR:dormant-SDR.icc", "remove:HDR:dormant-HDR.icc" }), "Scope is enabled before reading and removing dormant user associations");
profiles.User = false; profiles.Calls.Clear();
applied = await SceneService.Apply(backend, scene);
Check(applied.Issues.Count == 0 && profiles.Calls.SequenceEqual(new[] { "scope:True" }), "An empty user scope is enabled even when neither default needs a write");

// MainWindow undo saves Capture before applying, then applies that snapshot.
profiles = new FakeProfileOperations(); backend = ProfileBackend(profiles);
var undoSnapshot = await SceneService.Capture(backend, "Previous settings");
var namedProfiles = new Preset { Displays = new() { new() { Id = "A", UserProfiles = true, SdrProfile = "SDR.icc", HdrProfile = "HDR.icc" } } };
applied = await SceneService.Apply(backend, namedProfiles);
Check(applied.Issues.Count == 0 && profiles.Sdr.Single() == "SDR.icc" && profiles.Hdr.Single() == "HDR.icc", "Apply selects named SDR and HDR profiles through injected operations");
applied = await SceneService.Apply(backend, undoSnapshot);
Check(applied.Issues.Count == 0 && profiles.User && profiles.Sdr.Count == 0 && profiles.Hdr.Count == 0, "Undo restores captured absence for both defaults");
profiles.User = false;
undoSnapshot = await SceneService.Capture(backend, "Previous system scope");
await SceneService.Apply(backend, namedProfiles);
profiles.Calls.Clear();
applied = await SceneService.Apply(backend, undoSnapshot);
Check(applied.Issues.Count == 0 && !profiles.User && profiles.Calls.SequenceEqual(new[] { "scope:False" }), "Undo restores system scope without editing system defaults");

profiles.User = true; profiles.Calls.Clear();
var unspecified = new Preset { Displays = new() { new() { Id = "A", UserProfiles = true } } };
var unspecifiedRoundtrip = JsonSerializer.Deserialize<Preset>(JsonSerializer.Serialize(unspecified, Storage.Json), Storage.Json)!;
applied = await SceneService.Apply(backend, unspecifiedRoundtrip);
Check(applied.Issues.Count == 0 && unspecifiedRoundtrip.Displays[0].SdrProfile == null && profiles.Calls.Count == 0 && profiles.Sdr.Single() == "SDR.icc" && profiles.Hdr.Single() == "HDR.icc", "Null defaults survive persistence and leave existing defaults unspecified");
unspecified.Displays[0].UserProfiles = null;
unspecified.Displays[0].SdrProfile = "";
await SceneService.Apply(backend, unspecified);
Check(profiles.Calls.Count == 0, "An unspecified scope never writes ICC settings");
unspecified.Displays[0].UserProfiles = true;
applied = await SceneService.Apply(backend, unspecified);
Check(applied.Issues.Count == 0 && profiles.Sdr.Count == 0 && profiles.Hdr.Single() == "HDR.icc", "An empty SDR default and an unspecified HDR default remain distinct");

foreach (string failure in new[] { "remove", "ignored", "scope", "unavailable", "set" })
{
    profiles = new FakeProfileOperations(); profiles.Sdr.Add("assigned.icc");
    backend = ProfileBackend(profiles);
    profiles.FailRemove = failure == "remove";
    profiles.IgnoreWrites = failure is "ignored" or "scope" or "set";
    profiles.User = failure != "scope";
    profiles.Available = failure != "unavailable";
    var restore = new Preset { Displays = new() { new() { Id = "A", UserProfiles = true, SdrProfile = failure == "set" ? "other.icc" : "" } } };
    applied = await SceneService.Apply(backend, restore);
    Check(applied.Applied == 0 && applied.Issues.Count == 1 && applied.Issues[0].Contains("ICC"), "ICC restoration reports native/readback failure: " + failure);
    Check(profiles.Calls.Count <= 1, "Failed restoration is bounded and never reports fabricated success: " + failure);
}

Check(InputRules.Parse("(model(60(99))vcp(10 12 60(0F 10 11 12) D6(01 04)))").SequenceEqual(new uint[] { 15, 16, 17, 18 }), "Input parsing reads only reported VCP choices");
Check(InputRules.Parse("(vcp(10 60(11 11 12)))").SequenceEqual(new uint[] { 17, 18 }), "Repeated input choices are deduplicated");
Check(InputRules.Parse("(vcp( \t10 12(01 02)\n60 (0f 10) D6(01 04) \r\n))").SequenceEqual(new uint[] { 15, 16 }), "Complete VCP lists retain whitespace, lowercase values and unrelated features");
foreach (var malformed in new[] {
    "(vcp(10(01 60(11 12) 02)))",
    "(vcp(10(01 60(11 12) 02) 60(0F 10)))",
    "(vcp(garbage 60(11 12)))",
    "(vcp(60(11 12) garbage))",
    "(vcp(60(11 12) 10(01 (02))))"
})
    Check(InputRules.Parse(malformed).Length == 0, "Malformed VCP features never expose input-switch choices: " + malformed);
InputConnectionTests.Run(Check);
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
await CommandPipeTests.Run(Check);
var service = new DisplayService();
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); bool stopped = false;
    try { await service.SetPercentAsync(new DisplayInfo { Id = "not-a-real-device" }, 0x10, 50, cancelled.Token); }
    catch (OperationCanceledException) { stopped = true; }
    Check(stopped && !service.IsWriting, "Cancelled reconnect write exits before device lookup and releases its busy state");
}
await SceneDuplicateTests.Run(Check);
await SceneDraftTests.Run(Check);
await SceneOrderTests.Run(Check);
await SceneNoteTests.Run(Check);
SceneSearchTests.Run(Check);
await ReconnectTests.RunAsync(Check);
PipBufferTests.Run(Check);
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
    public FakeProfileOperations? ProfileOperations;
    public int StaleReferences;
    public Task<DisplayColorState> Color(DisplayInfo d) => FailColor ? Task.FromException<DisplayColorState>(new InvalidOperationException("No color API")) : Task.FromResult(ProfileOperations?.Read(d) ?? new DisplayColorState { HdrKnown = true, HdrSupported = true, HdrUserEnabled = false, ProfilesAvailable = true, UseUserProfiles = true, SdrProfile = "sRGB.icm" });
    private void Call(string name, DisplayInfo d) { Calls.Add(name + ":" + d.Id); if (!Screens.Contains(d)) StaleReferences++; }
    public Task Mode(DisplayInfo d, DisplayMode mode, uint orientation)
    {
        Call("mode", d); Screens[Screens.IndexOf(d)] = new() { Id = d.Id, Name = d.Name, Current = mode, Orientation = orientation, X = d.X, Y = d.Y, Primary = d.Primary, Brightness = d.Brightness, DpiScale = d.DpiScale }; return Task.CompletedTask;
    }
    public Task Dpi(DisplayInfo d, int percent) { Call("dpi", d); d.DpiScale.CurrentPercent = percent; return Task.CompletedTask; }
    public Task Hdr(DisplayInfo d, bool enabled) { Call("hdr", d); return Task.CompletedTask; }
    public Task Profiles(DisplayInfo d, PresetDisplay saved)
    {
        Call("profiles", d);
        if (ProfileOperations != null) AdvancedColorService.RestoreProfiles(d, saved.UserProfiles, saved.SdrProfile, saved.HdrProfile, ProfileOperations);
        return Task.CompletedTask;
    }
    public Task Brightness(DisplayInfo d, PresetDisplay saved) { Call("brightness", d); if (FailBrightness.Contains(d.Id)) throw new InvalidOperationException("Disconnected"); return Task.CompletedTask; }
    public Task Layout(IReadOnlyList<ScreenPlacement> placements) { Calls.Add("layout"); return Task.CompletedTask; }
}

internal sealed class FakeProfileOperations : IDisplayProfileOperations
{
    public bool User = true, Available = true, FailRemove, IgnoreWrites;
    public List<string> Sdr = new(), Hdr = new(), Calls = new();
    public DisplayColorState Read(DisplayInfo display) => new()
    {
        ProfilesAvailable = Available, ProfileUnavailableReason = "Unavailable ICC API", UseUserProfiles = User,
        SdrProfile = User ? Sdr.FirstOrDefault() ?? "" : "system-SDR.icc",
        HdrProfile = User ? Hdr.FirstOrDefault() ?? "" : "system-HDR.icc"
    };
    public void SetScope(DisplayInfo display, bool user)
    {
        Calls.Add("scope:" + user);
        if (!IgnoreWrites) User = user;
    }
    public void SetDefault(DisplayInfo display, string name, bool hdr)
    {
        if (!User || string.IsNullOrEmpty(name)) throw new Exception("Invalid default write");
        Calls.Add($"set:{(hdr ? "HDR" : "SDR")}:{name}");
        if (!IgnoreWrites) { var list = hdr ? Hdr : Sdr; list.Remove(name); list.Insert(0, name); }
    }
    public void RemoveAssociation(DisplayInfo display, string name, bool hdr)
    {
        if (!User || string.IsNullOrEmpty(name)) throw new Exception("Invalid association removal");
        Calls.Add($"remove:{(hdr ? "HDR" : "SDR")}:{name}");
        if (FailRemove) throw new InvalidOperationException("Native removal failed");
        if (!IgnoreWrites) (hdr ? Hdr : Sdr).Remove(name);
    }
}
