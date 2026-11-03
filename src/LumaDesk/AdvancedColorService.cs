// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

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
    public string ColorMode { get; set; } = L.T("s4d8c1c5b42");
    public uint BitsPerChannel { get; set; }
    public string Encoding { get; set; } = L.T("s4d8c1c5b42");
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
            CheckWin32(status, L.T("s5ee74333c6"));
            if (pc > 128 || mc > 1024) throw new InvalidOperationException(L.T("s8833926459"));
            var paths = new DpiNative.Path[pc]; var modes = new DpiNative.Mode[mc];
            status = DpiNative.QueryDisplayConfig(2, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (status == 122) continue;
            CheckWin32(status, L.T("s0b3152424b"));
            var matches = new List<DpiNative.Path>();
            foreach (var path in paths.Take((int)pc))
            {
                var name = new DpiNative.SourceName { Header = DpiNative.Packet<DpiNative.SourceName>(1, path.Source.Adapter, path.Source.Id) };
                if (DpiNative.GetSourceName(ref name) == 0 && string.Equals(name.Name, display.Device, StringComparison.OrdinalIgnoreCase)) matches.Add(path);
            }
            if (matches.Count != 1) throw new InvalidOperationException(matches.Count > 1 ? L.T("see3246b1fd") : L.T("s988c6e594e"));
            return matches[0];
        }
        throw new InvalidOperationException(L.T("s3bffd3b4df"));
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
                CheckHr(AdvancedColorNative.ColorProfileGetDisplayUserScope(path.Target.Adapter, path.Source.Id, out int scope), L.T("s34f9be9568"));
                state.UseUserProfiles = scope == 1;
                state.SdrProfile = DefaultProfile(path, scope, false);
                state.HdrProfile = DefaultProfile(path, scope, true);
                var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IntPtr list = IntPtr.Zero;
                try
                {
                    CheckHr(AdvancedColorNative.ColorProfileGetDisplayList(scope, path.Target.Adapter, path.Source.Id, out list, out uint count), L.T("sa58db507fa"));
                    if (count > 4096) throw new InvalidOperationException(L.T("sbe31adde5e"));
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
            catch (EntryPointNotFoundException) { state.ProfileUnavailableReason = L.T("s37bf50ef90"); }
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
            CheckWin32(AdvancedColorNative.GetLegacy(ref old), L.T("s9c07fd83a2"));
            state.BitsPerChannel = old.BitsPerChannel; state.Encoding = EncodingName(old.Encoding);
            state.ColorMode = (old.Flags & 2) != 0 ? L.T("s35c3003ca0") : L.T("s3a484cff6c");
            state.HdrUnavailableReason = L.T("s6932b13f73");
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
        state.ColorMode = mode switch { 0 => "SDR", 1 => L.T("s8a1e6f1c35"), 2 => "HDR", _ => L.T("s374c506372") };
        state.HdrUnavailableReason = !state.HdrSupported ? L.T("s40d62b3153") : state.PolicyLimited ? L.T("s14d2a76cf5") : "";
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
            CheckWin32(AdvancedColorNative.SetHdr(ref packet), L.T("s5efbcc24e6"));
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Thread.Sleep(100);
                var result = Read(display);
                if (result.HdrKnown && result.HdrUserEnabled == enabled) return;
            }
            throw new InvalidOperationException(L.T("s82559e2094"));
        }
    }

    public static void SetDefaultProfile(DisplayInfo display, string profileName, bool hdr)
    {
        lock (Gate)
        {
            string name = Path.GetFileName(profileName);
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, profileName, StringComparison.Ordinal)) throw new InvalidOperationException(L.T("s58d208dc77"));
            string file = Path.Combine(ColorDirectory(), name);
            if (!IsDisplayProfile(file)) throw new InvalidOperationException(L.T("s5b23d417da"));
            var before = Read(display);
            if (!before.ProfilesAvailable) throw new InvalidOperationException(before.ProfileUnavailableReason);
            if (hdr && (!before.HdrKnown || !before.HdrSupported)) throw new InvalidOperationException(L.T("s1fac9623dc"));
            var path = FindPath(display);
            string wcsDevice = FindWcsDevice(display, before.UseUserProfiles);
            string previous = hdr ? before.HdrProfile : before.SdrProfile;
            bool scopeChanged = false;
            try
            {
                if (!before.UseUserProfiles)
                {
                    CheckBool(AdvancedColorNative.WcsSetUsePerUserProfiles(wcsDevice, MonitorClass, true), L.T("sb16b8a2817")); scopeChanged = true;
                }
                // Associate without changing default first, then select the explicit SDR/HDR subtype.
                CheckHr(AdvancedColorNative.ColorProfileAddDisplayAssociation(1, name, path.Target.Adapter, path.Source.Id, false, hdr), L.T("s7959f6b82f"));
                CheckHr(AdvancedColorNative.ColorProfileSetDisplayDefaultAssociation(1, name, 0, hdr ? 8 : 7, path.Target.Adapter, path.Source.Id), L.T("sf8cc0b8acc"));
                var result = Read(display);
                if (!result.ProfilesAvailable || !result.UseUserProfiles || !string.Equals(hdr ? result.HdrProfile : result.SdrProfile, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.T("s7b37b0e749"));
            }
            catch
            {
                try
                {
                    path = FindPath(display); // Remap before restoring; never use a reused display identifier.
                    if (scopeChanged) CheckBool(AdvancedColorNative.WcsSetUsePerUserProfiles(wcsDevice, MonitorClass, false), L.T("s16bada7b8a"));
                    else if (!string.IsNullOrEmpty(previous)) CheckHr(AdvancedColorNative.ColorProfileSetDisplayDefaultAssociation(1, previous, 0, hdr ? 8 : 7, path.Target.Adapter, path.Source.Id), L.T("sce2729691e"));
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
                status?.Invoke(L.T("s4efaebcfc1"));
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
            if (!IsDisplayProfile(source)) throw new InvalidOperationException(L.T("s1f36d4b219"));
            string installed = Path.Combine(ColorDirectory(), Path.GetFileName(source));
            if (File.Exists(installed))
            {
                using var a = File.OpenRead(source); using var b = File.OpenRead(installed);
                if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new InvalidOperationException(L.T("s9fdfb960b5"));
                return;
            }
            if (!AdvancedColorNative.InstallColorProfileW(null, source))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error == 0 ? 31 : error, L.T("s141d362ce1") + new Win32Exception(error).Message);
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
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException(L.T("sbc4c62685a"));
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("se8f9d72e96"));
        try
        {
            using var helper = Process.Start(CreateProfileInstallStartInfo(executable, profile)) ?? throw new InvalidOperationException(L.T("s39c3fae14b"));
            if (!helper.WaitForExit(60000)) throw new InvalidOperationException(L.T("s4cde55d0cb"));
            if (helper.ExitCode != 0) throw new InvalidOperationException(L.F("s1fad7c9ede", helper.ExitCode) + new Win32Exception(helper.ExitCode).Message);
            // Verify actual installed bytes rather than relying on the helper exit.
            string installed = Path.Combine(ColorDirectory(), Path.GetFileName(profile));
            if (!File.Exists(installed)) throw new InvalidOperationException(L.T("s5309845c41"));
            using var a = File.OpenRead(profile); using var b = File.OpenRead(installed);
            if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new InvalidOperationException(L.T("s6c9bbb3bf5"));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException(L.T("s95bb15f7cf"), ex); }
    }

    public static void UseSystemProfiles(DisplayInfo display)
    {
        lock (Gate)
        {
            var before = Read(display);
            if (!before.ProfilesAvailable) throw new InvalidOperationException(before.ProfileUnavailableReason);
            if (!before.UseUserProfiles) return;
            FindPath(display);
            CheckBool(AdvancedColorNative.WcsSetUsePerUserProfiles(FindWcsDevice(display, true), MonitorClass, false), L.T("s16bada7b8a"));
            var result = Read(display);
            if (!result.ProfilesAvailable || result.UseUserProfiles) throw new InvalidOperationException(L.T("s17b3b61bdb"));
        }
    }

    private static string FindWcsDevice(DisplayInfo display, bool expectedScope)
    {
        // Use the physical monitor's DeviceKey from EnumDisplayDevices, never
        // \\.\DISPLAYn: WCS can accept that alias yet return an unrelated default.
        string name = display.RegistryPath;
        if (!string.IsNullOrWhiteSpace(name) && AdvancedColorNative.WcsGetUsePerUserProfiles(name, MonitorClass, out bool enabled) && enabled == expectedScope) return name;
        throw new InvalidOperationException(L.T("s8bf4a84f50"));
    }

    private static string DefaultProfile(DpiNative.Path path, int scope, bool hdr)
    {
        IntPtr name = IntPtr.Zero;
        try
        {
            int hr = AdvancedColorNative.ColorProfileGetDisplayDefault(scope, path.Target.Adapter, path.Source.Id, 0, hdr ? 8 : 7, out name);
            if (hr == unchecked((int)0x80070002) || hr == unchecked((int)0x80070490)) return "";
            CheckHr(hr, L.T("s4502606c47"));
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
        CheckBool(AdvancedColorNative.GetColorDirectoryW(null, buffer, ref bytes), L.T("sd0f1b580f5"));
        return buffer.ToString();
    }
    private static string EncodingName(uint value) => value switch { 0 => "RGB", 1 => "YCbCr 4:4:4", 2 => "YCbCr 4:2:2", 3 => "YCbCr 4:2:0", 4 => L.T("sa2846644a6"), _ => L.T("s4d8c1c5b42") };
    private static void CheckWin32(int result, string operation) { if (result != 0) throw new InvalidOperationException(operation + L.T("s09a4e5504e") + new Win32Exception(result).Message + $"（{result}）"); }
    private static void CheckBool(bool success, string operation) { if (!success) { int error = Marshal.GetLastWin32Error(); CheckWin32(error == 0 ? 31 : error, operation); } }
    private static void CheckHr(int result, string operation) { if (result < 0) throw new InvalidOperationException(operation + L.F("s59da614de2", result) + Marshal.GetExceptionForHR(result)?.Message); }
}
