// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;

int passed = 0;
void Check(bool result, string name) { if (!result) throw new Exception("FAIL: " + name); passed++; }
Check(Marshal.SizeOf<DpiNative.Header>() == 20, "CCD header size");
Check(Marshal.SizeOf<AdvancedColorNative.Info2>() == 36, "Info2 UINT32 fields on x64");
Check(Marshal.OffsetOf<AdvancedColorNative.Info2>(nameof(AdvancedColorNative.Info2.Flags)).ToInt32() == 20, "flags offset");
Check(Marshal.OffsetOf<AdvancedColorNative.Info2>(nameof(AdvancedColorNative.Info2.ActiveMode)).ToInt32() == 32, "active mode offset");
Check(Marshal.SizeOf<AdvancedColorNative.LegacyInfo>() == 32, "legacy size");
Check(Marshal.SizeOf<AdvancedColorNative.ValuePacket>() == 24, "HDR/white packet size");
var state = new DisplayColorState();
AdvancedColorService.DecodeHdr(0xC3, 1, state);
Check(!state.HdrSupported && !state.HdrUserEnabled && !state.HdrActive, "WCG is not HDR");
AdvancedColorService.DecodeHdr(0x30, 0, state);
Check(state.HdrSupported && state.HdrUserEnabled && !state.HdrActive, "enabled but not active preserved");
AdvancedColorService.DecodeHdr(0x38, 2, state);
Check(state.HdrActive && state.PolicyLimited, "active HDR and policy distinguished");
string temp = Path.Combine(Path.GetTempPath(), "LumaDesk-color-tests-" + Guid.NewGuid().ToString("N") + ".icc");
try
{
    byte[] header = new byte[128]; BinaryPrimitives.WriteUInt32BigEndian(header, 128);
    "mntr"u8.CopyTo(header.AsSpan(12)); "RGB "u8.CopyTo(header.AsSpan(16)); "acsp"u8.CopyTo(header.AsSpan(36));
    File.WriteAllBytes(temp, header); Check(AdvancedColorService.IsDisplayProfile(temp), "recognizes display RGB header");
    "prtr"u8.CopyTo(header.AsSpan(12)); File.WriteAllBytes(temp, header); Check(!AdvancedColorService.IsDisplayProfile(temp), "reject printer profile");
    "mntr"u8.CopyTo(header.AsSpan(12)); "CMYK"u8.CopyTo(header.AsSpan(16)); File.WriteAllBytes(temp, header); Check(!AdvancedColorService.IsDisplayProfile(temp), "reject non RGB");
    "RGB "u8.CopyTo(header.AsSpan(16)); BinaryPrimitives.WriteUInt32BigEndian(header, 4096); File.WriteAllBytes(temp, header); Check(!AdvancedColorService.IsDisplayProfile(temp), "reject truncated profile");
    bool refused = false;
    try { AdvancedColorService.InstallProfileOnly(temp); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "install-only rejects malformed file before native install");
}
finally { File.Delete(temp); }
string commandProfile = Path.Combine(Path.GetTempPath(), "色彩 文件 & test.icc");
var command = AdvancedColorService.CreateProfileInstallStartInfo(@"C:\Program Files\LumaDesk\LumaDesk.exe", commandProfile);
Check(command.ArgumentList.Count == 2 && command.ArgumentList[0] == "--install-color-profile", "fixed helper action");
Check(command.ArgumentList[1] == Path.GetFullPath(commandProfile) && command.Arguments == "", "profile path passed as single ArgumentList item");
Check(command.UseShellExecute && command.Verb == "runas" && command.WindowStyle == System.Diagnostics.ProcessWindowStyle.Hidden, "UAC helper only with hidden window");
Console.WriteLine($"PASS {passed} color rules/layout checks. No color/HDR writes.");
if (args.Contains("--probe"))
{
    var displays = await new DisplayService().ScanAsync();
    foreach (var display in displays)
    {
        var color = AdvancedColorService.Read(display);
        bool scopeReadable = AdvancedColorNative.WcsGetUsePerUserProfiles(display.Device, 0x6D6E7472, out bool userScope);
        bool keyReadable = AdvancedColorNative.WcsGetUsePerUserProfiles(display.RegistryPath, 0x6D6E7472, out bool keyScope);
        Console.WriteLine(JsonSerializer.Serialize(new { display.Name, display.Device, Color = color, GdiScopeReadable = scopeReadable, GdiUserScope = userScope, RegistryScopeReadable = keyReadable, RegistryUserScope = keyScope }));
    }
}
