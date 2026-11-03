// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;

namespace LumaDesk;

internal static class DriverInstaller
{
    internal const string Version = "25.7.23";
    internal const string DownloadUrl = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip";
    internal const string ArchiveHash = "E24210692B442B39AF763536330CE78B423F19342B7A7792C26DE3944E418B3A";
    internal static readonly IReadOnlyDictionary<string, string> FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mttvdd.cat"] = "08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6",
        ["MttVDD.dll"] = "C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05",
        ["MttVDD.inf"] = "550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D",
        ["vdd_settings.xml"] = "EDB2501D6D5DA17F66D15D4B97A6F4A3F0D8963165AC4A6A6259D95118288020"
    };
    internal static bool MatchesHash(byte[] bytes, string expected) => Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    internal static async Task<string> PreparePackageAsync()
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException(L.T("sefa2f6017b"));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LumaDeskWin/0.3");
        byte[] bytes = await client.GetByteArrayAsync(DownloadUrl);
        if (bytes.Length > 2 * 1024 * 1024 || !MatchesHash(bytes, ArchiveHash)) throw new InvalidDataException(L.T("sa00feccffc"));
        string destination = Path.Combine(Storage.Folder, "driver-cache", Version, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        foreach (var file in FileHashes)
        {
            var entry = archive.GetEntry("VirtualDisplayDriver/" + file.Key) ?? throw new InvalidDataException(L.T("sa6131ff9aa") + file.Key);
            using var stream = entry.Open(); using var data = new MemoryStream(); await stream.CopyToAsync(data);
            byte[] content = data.ToArray();
            if (!MatchesHash(content, file.Value)) throw new InvalidDataException(L.T("s11b81f5079") + file.Key);
            await File.WriteAllBytesAsync(Path.Combine(destination, file.Key), content);
        }
        VerifySignature(Path.Combine(destination, "mttvdd.cat"));
        VerifySignature(Path.Combine(destination, "MttVDD.dll"));
        return destination;
    }
    internal static async Task InstallAsync(string packageFolder)
        => await RunElevatedAsync("--install-virtual-driver", packageFolder);
    internal static async Task SetEnabledAsync(bool enabled)
        => await RunElevatedAsync("--set-virtual-driver-enabled", enabled ? "true" : "false");
    private static async Task RunElevatedAsync(string command, string argument)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(command); start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException(L.T("s0a8867cc2d"));
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException(L.T("s08dadacc38"));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new InvalidOperationException(L.T("s3749ec47d3")); }
    }
    // Invoked only by this application's explicit --install-virtual-driver mode.
    // All elevated inputs are byte-verified against a fixed official release.
    internal static void InstallVerifiedPackage(string packageFolder)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException(L.T("s4bcf77c213"));
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException(L.T("s8de5717282"));
        if (VirtualDisplayService.IsInstalled()) throw new InvalidOperationException(L.T("s9f42b7f1d6"));
        var contents = new Dictionary<string, byte[]>();
        foreach (var file in FileHashes)
        {
            string source = Path.Combine(packageFolder, file.Key);
            if (new FileInfo(source).Length > 2 * 1024 * 1024) throw new InvalidDataException(L.T("s0c552d617e"));
            byte[] bytes = File.ReadAllBytes(source);
            if (!MatchesHash(bytes, file.Value)) throw new InvalidDataException(L.T("s1ebc7cea3e") + file.Key);
            contents[file.Key] = bytes;
        }
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "vcruntime140.dll"))) throw new InvalidOperationException(L.T("s650fcabe24"));
        string shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LumaDeskWin");
        EnsureProtectedDirectory(shared);
        string root = Path.Combine(shared, "VirtualDisplayDriver");
        EnsureProtectedDirectory(root);
        string stage = Path.Combine(root, "package-" + Guid.NewGuid().ToString("N"));
        EnsureProtectedDirectory(stage);
        foreach (var file in contents) File.WriteAllBytes(Path.Combine(stage, file.Key), file.Value);
        VerifySignature(Path.Combine(stage, "mttvdd.cat")); VerifySignature(Path.Combine(stage, "MttVDD.dll"));
        string config = Path.Combine(root, "configuration");
        EnsureProtectedDirectory(config);
        var security = new DirectoryInfo(config).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(config).SetAccessControl(security);
        var document = XDocument.Parse(Encoding.UTF8.GetString(contents["vdd_settings.xml"]));
        document.Root!.Element("monitors")!.Element("count")!.Value = "1";
        string settings = Path.Combine(config, "vdd_settings.xml");
        if (File.Exists(settings) && (File.GetAttributes(settings) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException(L.T("s39e53d8c74"));
        if (File.Exists(settings)) File.Copy(settings, settings + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak");
        document.Save(settings);
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = baseKey.CreateSubKey(@"SOFTWARE\MikeTheTech\VirtualDisplayDriver")) key.SetValue("VDDPATH", config);
        RegisterDriver(Path.Combine(stage, "MttVDD.inf"));
        SetDriverEnabled(false);
    }
    internal static void SetDriverEnabled(bool enabled)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException(L.T("sc7227565fb"));
        var displayClass = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
        IntPtr set = SetupDiGetClassDevs(ref displayClass, null, IntPtr.Zero, 2);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        bool found = false;
        try
        {
            for (uint i = 0; i < 128; i++)
            {
                var device = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref device)) { if (Marshal.GetLastWin32Error() == 259) break; throw new Win32Exception(Marshal.GetLastWin32Error()); }
                var ids = new byte[8192];
                if (!SetupDiGetDeviceRegistryProperty(set, ref device, 1, out _, ids, (uint)ids.Length, out var required)) continue;
                bool match = false;
                foreach (var id in Encoding.Unicode.GetString(ids, 0, (int)Math.Min(required, (uint)ids.Length)).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    if (id.Equals(VirtualDisplayService.HardwareId, StringComparison.OrdinalIgnoreCase)) match = true;
                if (!match) continue;
                found = true;
                var change = new PropertyChange { HeaderSize = 8, InstallFunction = 0x12, StateChange = enabled ? 1u : 2u, Scope = 1 };
                if (!SetupDiSetClassInstallParams(set, ref device, ref change, (uint)Marshal.SizeOf<PropertyChange>()) || !SetupDiCallClassInstaller(0x12, set, ref device)) throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("s2ddc3008a1"));
            }
            if (!found) throw new InvalidOperationException(L.T("s2e17ee9e7e"));
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    private static void EnsureProtectedDirectory(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException(L.T("saf1738dcbb"));
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
    private static void RegisterDriver(string inf)
    {
        var displayClass = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
        IntPtr set = SetupDiCreateDeviceInfoList(ref displayClass, IntPtr.Zero);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var device = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() }; bool registered = false;
        try
        {
            if (!SetupDiCreateDeviceInfo(set, "MttVDD", ref displayClass, "Virtual Display Driver", IntPtr.Zero, 1, ref device)) throw new Win32Exception(Marshal.GetLastWin32Error());
            byte[] id = Encoding.Unicode.GetBytes(VirtualDisplayService.HardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(set, ref device, 1, id, (uint)id.Length) || !SetupDiCallClassInstaller(0x19, set, ref device)) throw new Win32Exception(Marshal.GetLastWin32Error());
            registered = true;
            if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, VirtualDisplayService.HardwareId, inf, 0, out bool reboot)) throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("se0373e7082"));
            Storage.Log(reboot ? "Virtual driver installed; reboot required." : "Virtual driver installed.");
        }
        catch { if (registered) SetupDiRemoveDevice(set, ref device); throw; }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    internal static void VerifySignature(string file)
    {
        var info = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = Marshal.StringToCoTaskMemUni(file) };
        IntPtr filePtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(info, filePtr, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = filePtr, ProviderFlags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0) throw new InvalidDataException(L.F("sdbce82518c", Path.GetFileName(file), result));
        }
        finally { Marshal.FreeCoTaskMem(filePtr); Marshal.FreeCoTaskMem(info.Path); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyChange { public uint HeaderSize, InstallFunction, StateChange, Scope, HardwareProfile; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustFile { public uint Size; public IntPtr Path, Handle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    {
        public uint Size; public IntPtr Policy, Sip; public uint UiChoice, RevocationChecks, UnionChoice; public IntPtr File;
        public uint StateAction; public IntPtr StateData, Url; public uint ProviderFlags, UiContext; public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid guid, IntPtr hwnd);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo device);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceInfo device, uint property, out uint type, byte[] buffer, uint length, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiSetClassInstallParams(IntPtr set, ref DeviceInfo device, ref PropertyChange change, uint size);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiCreateDeviceInfo(IntPtr set, string name, ref Guid guid, string description, IntPtr hwnd, uint flags, ref DeviceInfo data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref DeviceInfo data, uint property, byte[] buffer, uint length);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref DeviceInfo data);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiRemoveDevice(IntPtr set, ref DeviceInfo data);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr hwnd, string hardwareId, string fullInfPath, uint flags, out bool rebootRequired);
}
