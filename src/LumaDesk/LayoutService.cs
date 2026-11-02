using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace LumaDesk;

internal sealed class SavedLayoutScreen
{
    public string Device { get; set; } = "";
    public string Id { get; set; } = "";
    public bool Primary { get; set; }
    public Native.DevMode Mode { get; set; }
}
internal static class LayoutService
{
    // Preserve the complete 48-byte native union, including target timing data.
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct ConfigMode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(16)] public ulong Data0;
        [FieldOffset(24)] public ulong Data1;
        [FieldOffset(32)] public ulong Data2;
        [FieldOffset(40)] public ulong Data3;
        [FieldOffset(48)] public ulong Data4;
        [FieldOffset(56)] public ulong Data5;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public DpiNative.Luid Adapter;
        [FieldOffset(28)] public int X;
        [FieldOffset(32)] public int Y;
    }
    [DllImport("user32.dll", EntryPoint = "QueryDisplayConfig")]
    private static extern int QueryConfig(uint flags, ref uint paths, [Out] DpiNative.Path[] pathArray, ref uint modes, [Out] ConfigMode[] modeArray, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "SetDisplayConfig")]
    private static extern int SetConfig(uint paths, [In] DpiNative.Path[] pathArray, uint modes, [In] ConfigMode[] modeArray, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    private static extern int ApplyAll(string? device, IntPtr mode, IntPtr window, uint flags, IntPtr param);
    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint paths, IntPtr pathArray, uint modes, IntPtr modeArray, uint flags);
    public static void SetTopology(bool extend)
    {
        uint topology = extend ? 4u : 2u;
        int result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, 0x40 | topology);
        if (result != 0) throw new InvalidOperationException($"驱动不支持此显示模式（{result}）。");
        result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, 0x80 | topology);
        if (result != 0) throw new InvalidOperationException($"无法切换显示模式（{result}）。");
    }
    public static void Apply(IReadOnlyList<DisplayInfo> displays, IEnumerable<ScreenPlacement> placements, bool testOnly = false)
    {
        var layout = LayoutRules.Normalize(placements);
        if (layout.Count != displays.Count || !layout.All(p => displays.Any(d => d.Id == p.Id)))
            throw new InvalidOperationException("显示器列表已变化，请重新读取排列。");
        var saved = displays.Select(d =>
        {
            DisplayService.ValidateIdentity(d.Device, d.Id);
            return new SavedLayoutScreen { Device = d.Device, Id = d.Id, Primary = d.Primary, Mode = DisplayService.CurrentMode(d.Device) };
        }).ToList();
        var next = saved.Select(s =>
        {
            var requested = layout.Single(p => p.Id == s.Id); var mode = s.Mode;
            if (mode.Width != requested.Width || mode.Height != requested.Height) throw new InvalidOperationException("屏幕尺寸已变化，请重新读取排列。");
            mode.X = requested.X; mode.Y = requested.Y; mode.Fields = 0x20 | 0x80000 | 0x100000;
            return new SavedLayoutScreen { Device = s.Device, Id = s.Id, Primary = requested.Primary, Mode = mode };
        }).ToList();
        var configuration = ValidateConfiguration(next);
        if (testOnly) return;
        string recovery = Prepare(saved);
        try
        {
            int status = SetConfig((uint)configuration.Paths.Length, configuration.Paths, (uint)configuration.Modes.Length, configuration.Modes, 0x20 | 0x80 | 0x200);
            if (status != 0) throw new InvalidOperationException($"Windows 未能应用完整屏幕排列（{status}）。");
            bool matches = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                matches = next.All(s =>
                {
                    DisplayService.ValidateIdentity(s.Device, s.Id);
                    var m = DisplayService.CurrentMode(s.Device);
                    var device = Native.DisplayDevice.Create();
                    bool primary = false;
                    for (uint i = 0; Native.EnumDisplayDevices(null, i, ref device, 0); i++, device = Native.DisplayDevice.Create())
                        if (device.Name == s.Device) { primary = (device.Flags & 4) != 0; break; }
                    return m.X == s.Mode.X && m.Y == s.Mode.Y && m.Width == s.Mode.Width && m.Height == s.Mode.Height && primary == s.Primary;
                });
                if (matches) break;
                Thread.Sleep(50);
            }
            if (!matches) throw new InvalidOperationException("实际排列与请求不一致，正在恢复原排列。");
            File.Delete(recovery);
        }
        catch
        {
            try { Stage(saved); File.Delete(recovery); } catch (Exception ex) { Storage.Log("Layout rollback: " + ex); }
            throw;
        }
    }
    private static (DpiNative.Path[] Paths, ConfigMode[] Modes) ValidateConfiguration(List<SavedLayoutScreen> screens)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int status = DpiNative.GetDisplayConfigBufferSizes(2, out uint pc, out uint mc);
            if (status != 0 || pc == 0 || pc > 256 || mc > 1024) throw new InvalidOperationException("无法读取活动屏幕排列。");
            var paths = new DpiNative.Path[pc]; var modes = new ConfigMode[mc];
            status = QueryConfig(2, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (status == 122) continue;
            if (status != 0) throw new InvalidOperationException($"读取排列失败（{status}）。");
            Array.Resize(ref paths, (int)pc); Array.Resize(ref modes, (int)mc);
            var changed = new HashSet<uint>();
            foreach (var screen in screens)
            {
                var matches = paths.Where(path =>
                {
                    var name = new DpiNative.SourceName { Header = DpiNative.Packet<DpiNative.SourceName>(1, path.Source.Adapter, path.Source.Id) };
                    return DpiNative.GetSourceName(ref name) == 0 && name.Name == screen.Device;
                }).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("复制屏幕不能独立排列，请先切换为扩展桌面。");
                uint index = matches[0].Source.ModeIndex;
                if (index >= modes.Length || modes[index].Type != 1 || !changed.Add(index)) throw new InvalidOperationException("显示源不支持独立排列。");
                modes[index].X = screen.Mode.X; modes[index].Y = screen.Mode.Y;
            }
            if (changed.Count != paths.Length) throw new InvalidOperationException("活动显示器列表已变化，请重新读取。");
            status = SetConfig(pc, paths, mc, modes, 0x20 | 0x40);
            if (status != 0) throw new InvalidOperationException($"驱动不支持此完整排列（{status}）。");
            return (paths, modes);
        }
        throw new InvalidOperationException("显示器连接正在变化，请稍后重试。");
    }
    private static void Stage(List<SavedLayoutScreen> screens)
    {
        // Validate all identities before staging any registry changes.
        foreach (var screen in screens) DisplayService.ValidateIdentity(screen.Device, screen.Id);
        foreach (var screen in screens.OrderByDescending(s => s.Primary))
        {
            var mode = screen.Mode;
            int result = Native.ChangeDisplaySettingsEx(screen.Device, ref mode, IntPtr.Zero, 1 | 0x10000000u | (screen.Primary ? 0x10u : 0u), IntPtr.Zero);
            if (result != 0) throw new InvalidOperationException($"无法保存屏幕排列（{result}）。");
        }
        int apply = ApplyAll(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (apply != 0) throw new InvalidOperationException($"无法应用屏幕排列（{apply}）。");
    }
    private static string Prepare(List<SavedLayoutScreen> screens)
    {
        Directory.CreateDirectory(Storage.Folder);
        string path = Path.Combine(Storage.Folder, "layout-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(screens, Storage.Json));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--watch-layout"); start.ArgumentList.Add(path);
        try { using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动排列恢复进程。"); }
        catch { File.Delete(path); throw; }
        return path;
    }
    public static void RestoreFile(string path)
    {
        if (!File.Exists(path)) return;
        try { Stage(JsonSerializer.Deserialize<List<SavedLayoutScreen>>(File.ReadAllText(path), Storage.Json)!); File.Delete(path); }
        catch (Exception ex) { Storage.Log("Layout recovery: " + ex); }
    }
}
