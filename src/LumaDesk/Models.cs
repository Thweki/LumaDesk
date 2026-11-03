// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LumaDesk;

public record DisplayMode(uint Width, uint Height, uint Frequency)
{
    public string Resolution => $"{Width} × {Height}";
    public override string ToString() => $"{Resolution} · {Frequency} Hz";
}
public sealed class DisplayInfo
{
    public string Id { get; set; } = "";
    public string Device { get; set; } = "";
    public string Name { get; set; } = "";
    public string Adapter { get; set; } = "";
    public string RegistryPath { get; set; } = "";
    public int Number { get; set; }
    public bool Primary { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public uint Orientation { get; set; }
    public DisplayMode Current { get; set; } = new(0, 0, 0);
    public uint PreferredWidth { get; set; }
    public uint PreferredHeight { get; set; }
    public DpiScaleInfo DpiScale { get; set; } = new();
    public List<DisplayMode> Modes { get; set; } = new();
    public int? Brightness { get; set; }
    public int? Contrast { get; set; }
    public int? Volume { get; set; }
    internal Dictionary<byte, uint> VcpMaximums { get; } = new();
    public bool HasDdc { get; set; }
    public bool HasWmi { get; set; }
    public string? WmiInstance { get; set; }
    public int Dimming { get; set; }
    public string BrightnessMethod => HasWmi ? L.T("sd58c035039") : Brightness.HasValue ? L.T("sab507029e7") : L.T("s214b837a12");
    public string Subtitle => $"{Current.Resolution}  ·  {Current.Frequency} Hz";
}
public sealed class DpiScaleInfo
{
    public int CurrentPercent { get; set; }
    public int RecommendedPercent { get; set; }
    public int[] SupportedPercents { get; set; } = Array.Empty<int>();
    public string UnavailableReason { get; set; } = L.T("sad3d4c4dea");
    public bool CanSet => SupportedPercents.Length > 0 && string.IsNullOrEmpty(UnavailableReason);
}
public static class DpiRules
{
    private static readonly int[] Percentages = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    public static DpiScaleInfo FromRelative(int minimum, int current, int maximum)
    {
        long recommendedIndex = -(long)minimum, currentIndex = recommendedIndex + current, maximumIndex = recommendedIndex + maximum;
        if (minimum > 0 || maximum < 0 || recommendedIndex >= Percentages.Length || maximumIndex >= Percentages.Length || currentIndex < 0 || currentIndex > maximumIndex)
            throw new InvalidOperationException(L.T("s4dd5e6b5f4"));
        return new DpiScaleInfo { CurrentPercent = Percentages[(int)currentIndex], RecommendedPercent = Percentages[(int)recommendedIndex], SupportedPercents = Percentages.Take((int)maximumIndex + 1).ToArray(), UnavailableReason = "" };
    }
    public static int ToRelative(DpiScaleInfo info, int percent)
    {
        if (!info.CanSet || !info.SupportedPercents.Contains(percent)) throw new InvalidOperationException(L.T("s2a664f3979"));
        int recommended = Array.IndexOf(Percentages, info.RecommendedPercent), target = Array.IndexOf(Percentages, percent);
        if (recommended < 0 || target < 0) throw new InvalidOperationException(L.T("s78964c0bbc"));
        return target - recommended;
    }
    public static string LooksLike(DisplayMode mode, int percent)
    {
        if (percent <= 0) return L.T("s4d8c1c5b42");
        return $"{Math.Round(mode.Width * 100.0 / percent, MidpointRounding.AwayFromZero):0} × {Math.Round(mode.Height * 100.0 / percent, MidpointRounding.AwayFromZero):0}";
    }
}
public sealed class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<PresetDisplay> Displays { get; set; } = new();
    public bool OnConnect { get; set; }
    public string? AtTime { get; set; }
    public string? OnPower { get; set; }
    public HotkeyBinding? Shortcut { get; set; }
}
public sealed class PresetDisplay
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Brightness { get; set; }
    public int Dimming { get; set; }
    public DisplayMode? Mode { get; set; }
    public uint? Orientation { get; set; }
    public int? Dpi { get; set; }
    public bool? Hdr { get; set; }
    public bool? UserProfiles { get; set; }
    public string? SdrProfile { get; set; }
    public string? HdrProfile { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool? Primary { get; set; }
}
public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; } = 3;
    public uint Key { get; set; }
}
public sealed class DisplayPreference
{
    public string Alias { get; set; } = "";
    public int Order { get; set; } = int.MaxValue;
    public bool ShowScaling { get; set; } = true;
    public int Offset { get; set; }
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public List<DisplayMode> Favorites { get; set; } = new();
}
public sealed class DisplayGroup
{
    public string Name { get; set; } = "";
    public List<string> Displays { get; set; } = new();
}
public sealed class Preferences
{
    public string Language { get; set; } = "auto";
    public bool CloseToTray { get; set; } = true;
    public bool SyncBrightness { get; set; }
    public bool Hotkeys { get; set; } = true;
    public bool ShowHelp { get; set; } = true;
    public HashSet<string> QuickCollapsedDisplays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? QuickMoreDisplayId { get; set; }
    public string? QuickSelectedDisplayId { get; set; }
    public List<Preset> Presets { get; set; } = new();
    public List<DisplayGroup> Groups { get; set; } = new();
    public Dictionary<string, DisplayPreference> DisplayOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool RestoreBrightness { get; set; } = true;
    public Dictionary<string, PresetDisplay> RememberedBrightness { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HotkeyBinding BrightnessUp { get; set; } = new() { Key = 0x26 };
    public HotkeyBinding BrightnessDown { get; set; } = new() { Key = 0x28 };
    public HotkeyBinding ResetDimmingKey { get; set; } = new() { Key = 0x30 };
    public string HotkeyTarget { get; set; } = "pointer";
    public string Theme { get; set; } = "auto";
}
internal static class Storage
{
    private static readonly object logGate = new();
    public static string Folder => Environment.GetEnvironmentVariable("LUMADESK_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaDesk");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    public static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder, "settings.json")), Json) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static void Save(Preferences preferences)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(preferences, Json));
        File.Move(path + ".tmp", path, true);
    }
    public static void Log(string message)
    {
        try
        {
            lock (logGate)
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) File.Move(path, Path.Combine(Folder, "app.previous.log"), true);
                File.AppendAllText(path, $"{DateTime.Now:O} {message}\n");
            }
        }
        catch { }
    }
}
public static class DisplayRules
{
    public static string MonitorInstanceId(string id)
    {
        if (id.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            var parts = id.Substring(4).Split('#');
            if (parts.Length >= 3) return string.Join("\\", parts.Take(3));
        }
        return id.Replace("MONITOR\\", "DISPLAY\\", StringComparison.OrdinalIgnoreCase);
    }
    public static uint ToHardware(int percent, uint max) => (uint)Math.Round(Math.Clamp(percent, 0, 100) * (double)max / 100);
    public static int ToPercent(uint value, uint max) => max == 0 ? 0 : (int)Math.Clamp(Math.Round(value * 100.0 / max), 0, 100);
    public static bool Supports(DisplayInfo display, DisplayMode mode) => display.Modes.Contains(mode);
    public static List<DisplayMode> NormalizeModes(IEnumerable<DisplayMode> modes) => modes.Where(m => m.Width >= 640 && m.Height >= 480 && m.Frequency > 1).Distinct().OrderByDescending(m => (long)m.Width * m.Height).ThenByDescending(m => m.Frequency).ToList();
}
