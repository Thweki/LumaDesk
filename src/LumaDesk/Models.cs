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
    public string BrightnessMethod => HasWmi ? "Windows 内置背光" : Brightness.HasValue ? "DDC/CI 硬件背光" : "硬件未提供亮度接口";
    public string Subtitle => $"{Current.Resolution}  ·  {Current.Frequency} Hz";
}
public sealed class DpiScaleInfo
{
    public int CurrentPercent { get; set; }
    public int RecommendedPercent { get; set; }
    public int[] SupportedPercents { get; set; } = Array.Empty<int>();
    public string UnavailableReason { get; set; } = "尚未读取缩放信息。";
    public bool CanSet => SupportedPercents.Length > 0 && string.IsNullOrEmpty(UnavailableReason);
}
public static class DpiRules
{
    private static readonly int[] Percentages = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    public static DpiScaleInfo FromRelative(int minimum, int current, int maximum)
    {
        long recommendedIndex = -(long)minimum, currentIndex = recommendedIndex + current, maximumIndex = recommendedIndex + maximum;
        if (minimum > 0 || maximum < 0 || recommendedIndex >= Percentages.Length || maximumIndex >= Percentages.Length || currentIndex < 0 || currentIndex > maximumIndex)
            throw new InvalidOperationException("当前缩放范围无法识别，暂不能应用新的比例。");
        return new DpiScaleInfo { CurrentPercent = Percentages[(int)currentIndex], RecommendedPercent = Percentages[(int)recommendedIndex], SupportedPercents = Percentages.Take((int)maximumIndex + 1).ToArray(), UnavailableReason = "" };
    }
    public static int ToRelative(DpiScaleInfo info, int percent)
    {
        if (!info.CanSet || !info.SupportedPercents.Contains(percent)) throw new InvalidOperationException("此屏幕不支持所选缩放比例。");
        int recommended = Array.IndexOf(Percentages, info.RecommendedPercent), target = Array.IndexOf(Percentages, percent);
        if (recommended < 0 || target < 0) throw new InvalidOperationException("无法识别缩放比例。");
        return target - recommended;
    }
    public static string LooksLike(DisplayMode mode, int percent)
    {
        if (percent <= 0) return "未知";
        return $"{Math.Round(mode.Width * 100.0 / percent, MidpointRounding.AwayFromZero):0} × {Math.Round(mode.Height * 100.0 / percent, MidpointRounding.AwayFromZero):0}";
    }
}
public sealed class Preset
{
    public string Name { get; set; } = "";
    public List<PresetDisplay> Displays { get; set; } = new();
}
public sealed class PresetDisplay
{
    public string Id { get; set; } = "";
    public int? Brightness { get; set; }
    public int Dimming { get; set; }
}
public sealed class Preferences
{
    public bool CloseToTray { get; set; } = true;
    public bool SyncBrightness { get; set; }
    public bool Hotkeys { get; set; } = true;
    public bool ShowHelp { get; set; } = true;
    public HashSet<string> QuickCollapsedDisplays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? QuickMoreDisplayId { get; set; }
    public List<Preset> Presets { get; set; } = new();
}
internal static class Storage
{
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
        try { Directory.CreateDirectory(Folder); File.AppendAllText(Path.Combine(Folder, "app.log"), $"{DateTime.Now:O} {message}\n"); } catch { }
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
