using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LumaDesk;

public sealed class DisplayColorState
{
    public bool HdrKnown { get; set; }
    public bool HdrSupported { get; set; }
    public bool HdrUserEnabled { get; set; }
    public bool HdrActive { get; set; }
    public bool PolicyLimited { get; set; }
    public string ColorMode { get; set; } = "未知";
    public uint BitsPerChannel { get; set; }
    public string Encoding { get; set; } = "未知";
    public double? SdrWhiteNits { get; set; }
    public string HdrUnavailableReason { get; set; } = "";
    public bool ProfilesAvailable { get; set; }
    public bool UseUserProfiles { get; set; }
    public string SdrProfile { get; set; } = "";
    public string HdrProfile { get; set; } = "";
    public string[] Profiles { get; set; } = Array.Empty<string>();
    public string ProfileUnavailableReason { get; set; } = "";
}

// Layouts and constants come from Microsoft's WinSDK wingdi.h / Icm.h.
// Advanced-color support alone is NOT proof that a display supports HDR.
internal static class AdvancedColorNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Info2
    { public DpiNative.Header Header; public uint Flags, Encoding, BitsPerChannel, ActiveMode; }
    [StructLayout(LayoutKind.Sequential)] internal struct LegacyInfo
    { public DpiNative.Header Header; public uint Flags, Encoding, BitsPerChannel; }
    [StructLayout(LayoutKind.Sequential)] internal struct ValuePacket
    { public DpiNative.Header Header; public uint Value; }
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetInfo2(ref Info2 packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetLegacy(ref LegacyInfo packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetWhite(ref ValuePacket packet);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] internal static extern int SetHdr(ref ValuePacket packet);
    [DllImport("mscms.dll")] internal static extern int ColorProfileGetDisplayUserScope(DpiNative.Luid adapter, uint source, out int scope);
    [DllImport("mscms.dll")] internal static extern int ColorProfileGetDisplayDefault(int scope, DpiNative.Luid adapter, uint source, int type, int subtype, out IntPtr profile);
    [DllImport("mscms.dll")] internal static extern int ColorProfileGetDisplayList(int scope, DpiNative.Luid adapter, uint source, out IntPtr profiles, out uint count);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode)] internal static extern int ColorProfileAddDisplayAssociation(int scope, string profile, DpiNative.Luid adapter, uint source, [MarshalAs(UnmanagedType.Bool)] bool setDefault, [MarshalAs(UnmanagedType.Bool)] bool extended);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode)] internal static extern int ColorProfileSetDisplayDefaultAssociation(int scope, string profile, int type, int subtype, DpiNative.Luid adapter, uint source);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsGetUsePerUserProfiles(string device, uint deviceClass, [MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsSetUsePerUserProfiles(string device, uint deviceClass, [MarshalAs(UnmanagedType.Bool)] bool enabled);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InstallColorProfileW(string? machine, string profile);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetColorDirectoryW(string? machine, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr memory);
}

internal static class AdvancedColorService
{
    private const uint MonitorClass = 0x6D6E7472; // 'mntr'
    private static readonly object Gate = new();

    internal static DpiNative.Path FindPath(DisplayInfo display)
    {
        DisplayService.ValidateIdentity(display.Device, display.Id);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int status = DpiNative.GetDisplayConfigBufferSizes(2, out uint pc, out uint mc);
            CheckWin32(status, "读取显示路径");
            if (pc > 128 || mc > 1024) throw new InvalidOperationException("显示路径数据超出支持范围。");
            var paths = new DpiNative.Path[pc]; var modes = new DpiNative.Mode[mc];
            status = DpiNative.QueryDisplayConfig(2, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (status == 122) continue;
            CheckWin32(status, "查询显示路径");
            var matches = new List<DpiNative.Path>();
            foreach (var path in paths.Take((int)pc))
            {
                var name = new DpiNative.SourceName { Header = DpiNative.Packet<DpiNative.SourceName>(1, path.Source.Adapter, path.Source.Id) };
                if (DpiNative.GetSourceName(ref name) == 0 && string.Equals(name.Name, display.Device, StringComparison.OrdinalIgnoreCase)) matches.Add(path);
            }
            if (matches.Count != 1) throw new InvalidOperationException(matches.Count > 1 ? "镜像中的屏幕共用显示源，暂不能单独配置颜色。" : "此屏幕没有唯一活动显示路径。");
            return matches[0];
        }
        throw new InvalidOperationException("显示器连接正在变化，请重新读取。");
    }

    public static DisplayColorState Read(DisplayInfo display)
    {
        lock (Gate)
        {
            var state = new DisplayColorState();
            DpiNative.Path path;
            try { path = FindPath(display); }
            catch (Exception ex) { state.HdrUnavailableReason = state.ProfileUnavailableReason = ex.Message; return state; }
            try { ReadHdr(path, state); } catch (Exception ex) { state.HdrUnavailableReason = ex.Message; }
            try
            {
                CheckHr(AdvancedColorNative.ColorProfileGetDisplayUserScope(path.Target.Adapter, path.Source.Id, out int scope), "读取颜色配置作用域");
                state.UseUserProfiles = scope == 1;
                state.SdrProfile = DefaultProfile(path, scope, false);
                state.HdrProfile = DefaultProfile(path, scope, true);
                var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IntPtr list = IntPtr.Zero;
                try
                {
                    CheckHr(AdvancedColorNative.ColorProfileGetDisplayList(scope, path.Target.Adapter, path.Source.Id, out list, out uint count), "读取屏幕颜色配置列表");
                    if (count > 4096) throw new InvalidOperationException("颜色配置列表超出支持范围。");
                    for (int i = 0; i < count; i++)
                    {
                        string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(list, checked(i * IntPtr.Size)));
                        if (!string.IsNullOrWhiteSpace(name)) profiles.Add(Path.GetFileName(name));
                    }
                }
                finally { if (list != IntPtr.Zero) AdvancedColorNative.LocalFree(list); }
                foreach (string file in Directory.EnumerateFiles(ColorDirectory()))
                    if (IsDisplayProfile(file)) profiles.Add(Path.GetFileName(file));
                if (!string.IsNullOrEmpty(state.SdrProfile)) profiles.Add(state.SdrProfile);
                if (!string.IsNullOrEmpty(state.HdrProfile)) profiles.Add(state.HdrProfile);
                state.Profiles = profiles.OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToArray();
                state.ProfilesAvailable = true;
            }
            catch (EntryPointNotFoundException) { state.ProfileUnavailableReason = "此 Windows 版本没有逐屏颜色配置接口，需要 Windows 11 或兼容系统。"; }
            catch (Exception ex) { state.ProfileUnavailableReason = ex.Message; }
            return state;
        }
    }

    private static void ReadHdr(DpiNative.Path path, DisplayColorState state)
    {
        var packet = new AdvancedColorNative.Info2 { Header = DpiNative.Packet<AdvancedColorNative.Info2>(15, path.Target.Adapter, path.Target.Id) };
        int status = AdvancedColorNative.GetInfo2(ref packet);
        if (status == 0)
        {
            DecodeHdr(packet.Flags, packet.ActiveMode, state);
            state.BitsPerChannel = packet.BitsPerChannel; state.Encoding = EncodingName(packet.Encoding);
        }
        else
        {
            var old = new AdvancedColorNative.LegacyInfo { Header = DpiNative.Packet<AdvancedColorNative.LegacyInfo>(9, path.Target.Adapter, path.Target.Id) };
            CheckWin32(AdvancedColorNative.GetLegacy(ref old), "读取高级颜色状态");
            state.BitsPerChannel = old.BitsPerChannel; state.Encoding = EncodingName(old.Encoding);
            state.ColorMode = (old.Flags & 2) != 0 ? "高级颜色（HDR 状态未确定）" : "标准颜色";
            state.HdrUnavailableReason = "系统只返回通用高级颜色状态，无法可靠区分 HDR 和广色域；HDR 开关需要支持独立 HDR 接口的 Windows。";
        }
        var white = new AdvancedColorNative.ValuePacket { Header = DpiNative.Packet<AdvancedColorNative.ValuePacket>(11, path.Target.Adapter, path.Target.Id) };
        if (state.HdrActive && AdvancedColorNative.GetWhite(ref white) == 0) state.SdrWhiteNits = white.Value * 0.08;
    }

    // Pure decoding is exposed for tests: WCG bits must never imply HDR support.
    internal static void DecodeHdr(uint flags, uint mode, DisplayColorState state)
    {
        state.HdrKnown = true; state.HdrSupported = (flags & 0x10) != 0;
        state.HdrUserEnabled = (flags & 0x20) != 0; state.HdrActive = mode == 2;
        state.PolicyLimited = (flags & 8) != 0;
        state.ColorMode = mode switch { 0 => "SDR", 1 => "广色域 SDR", 2 => "HDR", _ => "未知颜色模式" };
        state.HdrUnavailableReason = !state.HdrSupported ? "此显示路径未报告 HDR 支持。" : state.PolicyLimited ? "Windows 策略正在限制高级颜色。" : "";
    }

    public static void SetHdr(DisplayInfo display, bool enabled)
    {
        lock (Gate)
        {
            var before = Read(display);
            if (!before.HdrKnown || !before.HdrSupported || (enabled && before.PolicyLimited)) throw new InvalidOperationException(before.HdrUnavailableReason);
            if (before.HdrUserEnabled == enabled) return;
            var path = FindPath(display);
            var packet = new AdvancedColorNative.ValuePacket { Header = DpiNative.Packet<AdvancedColorNative.ValuePacket>(16, path.Target.Adapter, path.Target.Id), Value = enabled ? 1u : 0u };
            CheckWin32(AdvancedColorNative.SetHdr(ref packet), "切换 HDR");
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Thread.Sleep(100);
                var result = Read(display);
                if (result.HdrKnown && result.HdrUserEnabled == enabled) return;
            }
            throw new InvalidOperationException("Windows 尚未回报请求的 HDR 开关状态，请重新读取实际状态。");
        }
    }

    public static void SetDefaultProfile(DisplayInfo display, string profileName, bool hdr)
    {
        lock (Gate)
        {
            string name = Path.GetFileName(profileName);
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, profileName, StringComparison.Ordinal)) throw new InvalidOperationException("请选择已安装的配置文件名称。");
            string file = Path.Combine(ColorDirectory(), name);
            if (!IsDisplayProfile(file)) throw new InvalidOperationException("请选择有效的显示器 RGB ICC 配置文件。");
            var before = Read(display);
            if (!before.ProfilesAvailable) throw new InvalidOperationException(before.ProfileUnavailableReason);
            if (hdr && (!before.HdrKnown || !before.HdrSupported)) throw new InvalidOperationException("此显示器尚未确认支持 HDR 配置。");
            var path = FindPath(display);
            string wcsDevice = FindWcsDevice(display, before.UseUserProfiles);
            string previous = hdr ? before.HdrProfile : before.SdrProfile;
            bool scopeChanged = false;
            try
            {
                if (!before.UseUserProfiles)
                {
                    CheckBool(AdvancedColorNative.WcsSetUsePerUserProfiles(wcsDevice, MonitorClass, true), "启用此屏幕的个人颜色配置"); scopeChanged = true;
                }
                // Associate without changing default first, then select the explicit SDR/HDR subtype.
                CheckHr(AdvancedColorNative.ColorProfileAddDisplayAssociation(1, name, path.Target.Adapter, path.Source.Id, false, hdr), "关联颜色配置");
                CheckHr(AdvancedColorNative.ColorProfileSetDisplayDefaultAssociation(1, name, 0, hdr ? 8 : 7, path.Target.Adapter, path.Source.Id), "设置默认颜色配置");
                var result = Read(display);
                if (!result.ProfilesAvailable || !result.UseUserProfiles || !string.Equals(hdr ? result.HdrProfile : result.SdrProfile, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("默认颜色配置回读与请求不一致。");
            }
            catch
            {
                try
                {
                    path = FindPath(display); // Remap before restoring; never use a reused display identifier.
                    if (scopeChanged) CheckBool(AdvancedColorNative.WcsSetUsePerUserProfiles(wcsDevice, MonitorClass, false), "恢复系统颜色配置");
                    else if (!string.IsNullOrEmpty(previous)) CheckHr(AdvancedColorNative.ColorProfileSetDisplayDefaultAssociation(1, previous, 0, hdr ? 8 : 7, path.Target.Adapter, path.Source.Id), "恢复原默认配置");
                }
                catch (Exception ex) { Storage.Log("Color profile rollback: " + ex.Message); }
                throw;
            }
        }
    }

    public static void InstallAndSetProfile(DisplayInfo display, string file, bool hdr, Action<string>? status = null)
    {
        lock (Gate)
        {
            FindPath(display);
            string source = Path.GetFullPath(file);
            try { InstallProfileOnly(source); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                status?.Invoke("首次安装此颜色配置需要 Windows 系统授权；安装后会返回当前用户应用配置。 ");
                InstallWithElevation(source);
            }
            // The elevated helper installs only. Apply to the original user's scope
            // here, even if an administrator supplied different credentials to UAC.
            SetDefaultProfile(display, Path.GetFileName(source), hdr);
        }
    }

    // Fixed elevated action. Never associates a monitor or modifies user defaults.
    // Both parent and helper validate independently; a collision never overwrites.
    public static void InstallProfileOnly(string file)
    {
        lock (Gate)
        {
            string source = Path.GetFullPath(file);
            if (!IsDisplayProfile(source)) throw new InvalidOperationException("文件不是有效的显示器 RGB ICC/ICM 配置。");
            string installed = Path.Combine(ColorDirectory(), Path.GetFileName(source));
            if (File.Exists(installed))
            {
                using var a = File.OpenRead(source); using var b = File.OpenRead(installed);
                if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new InvalidOperationException("系统中已有同名但内容不同的配置；请为导入文件使用不同名称。");
                return;
            }
            if (!AdvancedColorNative.InstallColorProfileW(null, source))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error == 0 ? 31 : error, "安装颜色配置失败：" + new Win32Exception(error).Message);
            }
        }
    }

    internal static ProcessStartInfo CreateProfileInstallStartInfo(string executable, string profile)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--install-color-profile");
        start.ArgumentList.Add(Path.GetFullPath(profile));
        return start;
    }

    private static void InstallWithElevation(string profile)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法找到颜色配置安装程序。");
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请通过 LumaDesk.exe 启动应用后导入配置，以便请求 Windows 系统授权。");
        try
        {
            using var helper = Process.Start(CreateProfileInstallStartInfo(executable, profile)) ?? throw new InvalidOperationException("无法启动颜色配置安装程序。");
            if (!helper.WaitForExit(60000)) throw new InvalidOperationException("颜色配置安装尚未完成；完成后可再次选择导入。");
            if (helper.ExitCode != 0) throw new InvalidOperationException($"颜色配置安装未完成（{helper.ExitCode}）：" + new Win32Exception(helper.ExitCode).Message);
            // Verify actual installed bytes rather than relying on the helper exit.
            string installed = Path.Combine(ColorDirectory(), Path.GetFileName(profile));
            if (!File.Exists(installed)) throw new InvalidOperationException("安装程序已结束，但系统中没有找到配置文件。");
            using var a = File.OpenRead(profile); using var b = File.OpenRead(installed);
            if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new InvalidOperationException("安装后的配置与所选文件不一致，未应用到显示器。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException("已取消 Windows 系统授权，未应用颜色配置。", ex); }
    }

    public static void UseSystemProfiles(DisplayInfo display)
    {
        lock (Gate)
        {
            var before = Read(display);
            if (!before.ProfilesAvailable) throw new InvalidOperationException(before.ProfileUnavailableReason);
            if (!before.UseUserProfiles) return;
            FindPath(display);
            CheckBool(AdvancedColorNative.WcsSetUsePerUserProfiles(FindWcsDevice(display, true), MonitorClass, false), "恢复系统颜色配置");
            var result = Read(display);
            if (!result.ProfilesAvailable || result.UseUserProfiles) throw new InvalidOperationException("尚未确认恢复系统颜色配置，请重新读取。");
        }
    }

    private static string FindWcsDevice(DisplayInfo display, bool expectedScope)
    {
        // Use the physical monitor's DeviceKey from EnumDisplayDevices, never
        // \\.\DISPLAYn: WCS can accept that alias yet return an unrelated default.
        string name = display.RegistryPath;
        if (!string.IsNullOrWhiteSpace(name) && AdvancedColorNative.WcsGetUsePerUserProfiles(name, MonitorClass, out bool enabled) && enabled == expectedScope) return name;
        throw new InvalidOperationException("无法将此显示器对应到个人颜色配置作用域。");
    }

    private static string DefaultProfile(DpiNative.Path path, int scope, bool hdr)
    {
        IntPtr name = IntPtr.Zero;
        try
        {
            int hr = AdvancedColorNative.ColorProfileGetDisplayDefault(scope, path.Target.Adapter, path.Source.Id, 0, hdr ? 8 : 7, out name);
            if (hr == unchecked((int)0x80070002) || hr == unchecked((int)0x80070490)) return "";
            CheckHr(hr, "读取默认颜色配置");
            return Path.GetFileName(Marshal.PtrToStringUni(name) ?? "");
        }
        finally { if (name != IntPtr.Zero) AdvancedColorNative.LocalFree(name); }
    }

    internal static bool IsDisplayProfile(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            if (stream.Length < 128 || stream.Length > 64 * 1024 * 1024) return false;
            Span<byte> header = stackalloc byte[128]; stream.ReadExactly(header);
            uint length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
            return length >= 128 && length <= stream.Length && header.Slice(36, 4).SequenceEqual("acsp"u8) && header.Slice(12, 4).SequenceEqual("mntr"u8) && header.Slice(16, 4).SequenceEqual("RGB "u8);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string ColorDirectory()
    {
        uint bytes = 32768;
        var buffer = new StringBuilder((int)bytes / 2);
        CheckBool(AdvancedColorNative.GetColorDirectoryW(null, buffer, ref bytes), "读取颜色配置目录");
        return buffer.ToString();
    }
    private static string EncodingName(uint value) => value switch { 0 => "RGB", 1 => "YCbCr 4:4:4", 2 => "YCbCr 4:2:2", 3 => "YCbCr 4:2:0", 4 => "强度", _ => "未知" };
    private static void CheckWin32(int result, string operation) { if (result != 0) throw new InvalidOperationException(operation + "失败：" + new Win32Exception(result).Message + $"（{result}）"); }
    private static void CheckBool(bool success, string operation) { if (!success) { int error = Marshal.GetLastWin32Error(); CheckWin32(error == 0 ? 31 : error, operation); } }
    private static void CheckHr(int result, string operation) { if (result < 0) throw new InvalidOperationException(operation + $"失败（0x{result:X8}）：" + Marshal.GetExceptionForHR(result)?.Message); }
}
