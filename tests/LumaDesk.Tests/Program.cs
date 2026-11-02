using LumaDesk;
using System.Runtime.InteropServices;
using System.Text.Json;

int tests = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); tests++; Console.WriteLine("PASS " + name); }
Check(Marshal.SizeOf<Native.DevMode>() == 220, "DEVMODEW native layout is 220 bytes");
Check(Marshal.OffsetOf<Native.DevMode>(nameof(Native.DevMode.Width)).ToInt32() == 172, "DEVMODEW width offset");
Check(Marshal.SizeOf<Native.DisplayDevice>() == 840, "DISPLAY_DEVICEW native layout");
Check(Marshal.SizeOf<Native.MonitorInfo>() == 104, "MONITORINFOEXW native layout");
Check(DisplayRules.ToHardware(50, 255) == 128, "DDC percentage conversion uses hardware range");
Check(DisplayRules.ToHardware(-30, 100) == 0 && DisplayRules.ToHardware(150, 100) == 100, "DDC clamps out of range input");
Check(DisplayRules.ToPercent(128, 255) == 50 && DisplayRules.ToPercent(50, 0) == 0, "DDC read conversion and zero range");
Check(DisplayRules.MonitorInstanceId(@"\\?\DISPLAY#HKC33AC#5&abc&0&UID1#{guid}") == @"DISPLAY\HKC33AC\5&abc&0&UID1", "Monitor device interface maps to actual registry instance");
var modes = DisplayRules.NormalizeModes(new[] { new DisplayMode(1920,1080,60), new DisplayMode(3840,2160,60), new DisplayMode(1920,1080,144), new DisplayMode(1920,1080,60), new DisplayMode(320,200,60), new DisplayMode(1920,1080,0) });
Check(modes.Count == 3 && modes[0].Width == 3840 && modes[1].Frequency == 144, "Mode deduplication, filtering and ordering");
Check(!DisplayRules.Supports(new DisplayInfo { Modes = modes }, new(3840, 2160, 240)), "Unsupported display mode rejected");
int applied = 0, restored = 0;
var lease = new ModeLease(() => applied++, () => restored++); lease.Revert(); lease.Revert();
Check(applied == 1 && restored == 1 && !lease.Pending, "Timeout rollback is idempotent");
lease = new ModeLease(() => applied++, () => restored++); lease.Keep(); lease.Revert();
Check(restored == 1 && !lease.Pending, "Confirmation prevents later rollback");
try { _ = new ModeLease(() => throw new InvalidOperationException(), () => restored++); } catch (InvalidOperationException) { }
Check(restored == 2, "Failed apply attempts restore");
bool failRestore = true;
lease = new ModeLease(() => {}, () => { if (failRestore) throw new InvalidOperationException(); restored++; });
try { lease.Revert(); } catch (InvalidOperationException) { }
Check(lease.Pending, "Failed rollback remains pending"); failRestore = false; lease.Revert(); Check(!lease.Pending && restored == 3, "Failed rollback can retry");
var original = Native.DevMode.Create(); original.Width = 2560; original.X = -2560; original.Frequency = 144; original.Fields = 0x80000;
var copy = JsonSerializer.Deserialize<Native.DevMode>(JsonSerializer.Serialize(original, Storage.Json), Storage.Json);
Check(copy.Width == 2560 && copy.X == -2560 && copy.Size == 220 && copy.Fields == original.Fields, "Watchdog recovery data roundtrip preserves native mode");
var prefs = new Preferences { Presets = new() { new Preset { Name = "夜间", Displays = new() { new PresetDisplay { Id = "MONITOR\\A", Brightness = null, Dimming = 25 } } } } };
var restoredPrefs = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(prefs, Storage.Json), Storage.Json)!;
Check(restoredPrefs.Presets[0].Name == "夜间" && restoredPrefs.Presets[0].Displays[0].Brightness == null, "Presets roundtrip Chinese names and unsupported capabilities");
Check(Marshal.SizeOf<DpiNative.Header>() == 20 && Marshal.SizeOf<DpiNative.GetScale>() == 32 && Marshal.SizeOf<DpiNative.SetScale>() == 24, "DPI request native layouts");
Check(Marshal.SizeOf<DpiNative.Path>() == 72 && Marshal.SizeOf<DpiNative.Mode>() == 64 && Marshal.SizeOf<DpiNative.SourceName>() == 84 && Marshal.SizeOf<DpiNative.PreferredMode>() == 80, "CCD path and preferred mode native layouts");
var dpi = DpiRules.FromRelative(-2, 1, 4);
Check(dpi.RecommendedPercent == 150 && dpi.CurrentPercent == 175 && dpi.SupportedPercents.Last() == 250, "DPI relative values use recommended step as origin");
Check(DpiRules.ToRelative(dpi, 100) == -2 && DpiRules.ToRelative(dpi, 200) == 2, "DPI exact scale converts back to relative offset");
Check(DpiRules.LooksLike(new(2560,1440,280), 125) == "2048 × 1152" && DpiRules.LooksLike(new(3840,2160,144), 200) == "1920 × 1080", "Logical workspace retains physical pixel count");
Check(DpiRules.LooksLike(new(2560,1440,280), 150) == "1707 × 960", "Fractional scale preview rounds logical dimensions");
foreach (var invalid in new[] { (1, 1, 3), (-20, 0, 3), (-1, -2, 3), (-1, 9, 3), (int.MinValue, 0, int.MaxValue) })
{
    bool rejected = false; try { DpiRules.FromRelative(invalid.Item1, invalid.Item2, invalid.Item3); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Invalid or unknown DPI packet rejected " + invalid);
}
foreach (int percent in new[] { 0, 110, 275, 500 })
{
    bool rejected = false; try { DpiRules.ToRelative(dpi, percent); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Unsupported DPI choice rejected: " + percent);
}
var writes = new List<int>();
var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
var writer = new LatestValueWriter(async v => { writes.Add(v); if (v == 10) { started.SetResult(); await release.Task; } }, (_, ex) => { if (ex != null) throw ex; }, () => Task.CompletedTask);
writer.Request(10); await started.Task; writer.Request(20); writer.Request(30); release.SetResult(); await writer.WhenIdle;
Check(writes.SequenceEqual(new[] { 10, 30 }), "Slow slider writes serialize and coalesce intermediate values");
var delayGate = new TaskCompletionSource(); writes.Clear();
writer = new LatestValueWriter(v => { writes.Add(v); return Task.CompletedTask; }, (_, _) => { }, () => delayGate.Task);
writer.Request(10); writer.Request(50); writer.Stop(); delayGate.SetResult(); await writer.WhenIdle;
Check(writes.Count == 0, "Unloaded slider cancels unsent writes");
int errors = 0; writes.Clear();
writer = new LatestValueWriter(v => { if (v == 5) throw new InvalidOperationException(); writes.Add(v); return Task.CompletedTask; }, (_, ex) => { if (ex != null) errors++; }, () => Task.CompletedTask);
writer.Request(5); await writer.WhenIdle; writer.Request(55); await writer.WhenIdle;
Check(errors == 1 && writes.SequenceEqual(new[] { 55 }), "Slider writer reports failures and accepts later input");
Check(BrightnessRules.Split(100, true) == new CombinedLevel(100, 0) && BrightnessRules.Split(20, true) == new CombinedLevel(0, 0), "Combined brightness reaches full and minimum hardware backlight without dimming");
Check(BrightnessRules.Split(0, true) == new CombinedLevel(0, 85) && BrightnessRules.Split(10, true) == new CombinedLevel(0, 42), "Combined low range extends below backlight minimum");
Check(Enumerable.Range(0,101).All(v => { var level = BrightnessRules.Split(v, true); return Math.Abs(BrightnessRules.Combine(level.Hardware, level.Dimming)-v) <= 1; }), "Combined brightness mapping roundtrips full control range");
Check(BrightnessRules.Split(50, false).Hardware == null && BrightnessRules.Split(100, false).Dimming == 0 && BrightnessRules.Split(0, false).Dimming == 85, "Software-only display has a functional full-range combined control");
Check(BrightnessRules.Split(-5,true) == BrightnessRules.Split(0,true) && BrightnessRules.Split(150,true) == BrightnessRules.Split(100,true), "Combined brightness clamps extremes");
var layout = new[] { new ScreenPlacement("A", -1920,0,1920,1080,false), new ScreenPlacement("B",0,0,2560,1440,true) };
Check(LayoutRules.Normalize(layout).SequenceEqual(layout), "Valid adjacent monitors preserve pixel coordinates");
var primaryChanged = LayoutRules.Normalize(layout.Select(p => p with { Primary = p.Id == "A" }));
Check(primaryChanged[0].X == 0 && primaryChanged[1].X == 1920 && primaryChanged[0].Primary, "Changing primary translates the whole desktop while preserving relative positions");
foreach (var invalid in new[] {
    new[] { layout[0] with { Primary = true }, layout[1] },
    new[] { layout[0] with { X = -1000 }, layout[1] },
    new[] { layout[0] with { X = -2000 }, layout[1] },
    new[] { layout[0] with { Id = "B" }, layout[1] },
    new[] { layout[0] with { Width = 0 }, layout[1] }
})
{
    bool rejectedLayout = false; try { LayoutRules.Normalize(invalid); } catch (InvalidOperationException) { rejectedLayout = true; }
    Check(rejectedLayout, "Invalid duplicate-primary, overlap, gap, identity or size layout rejected");
}
var snapped = LayoutRules.Snap(layout[0] with { X = -1900, Y = 15 }, new[] { layout[1] }, 30);
Check(snapped.X == -1920 && snapped.Y == 0, "Dragged screen snaps to neighboring edge and alignment");
Console.WriteLine($"{tests} tests passed.");
