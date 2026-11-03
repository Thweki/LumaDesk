// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace LumaDesk;

internal sealed record VirtualDisplayState(bool Installed, bool Connected, int ActiveCount, int? ConfiguredCount, string Message, bool Disabled = false);

internal sealed class VirtualDisplayService
{
    internal const int MaximumDisplays = 8;
    internal const string HardwareId = @"Root\MttVDD";
    private readonly Func<string, CancellationToken, Task<string>> transport;
    private readonly SemaphoreSlim gate = new(1, 1);
    internal VirtualDisplayService(Func<string, CancellationToken, Task<string>>? transport = null) => this.transport = transport ?? SendPipeAsync;

    internal static string ConfigurationFolder
    {
        get
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\MikeTheTech\VirtualDisplayDriver");
            return key?.GetValue("VDDPATH") is string path && Path.IsPathFullyQualified(path) ? path : @"C:\VirtualDisplayDriver";
        }
    }
    internal static bool IsInstalled() => GetDriverState().Installed;
    internal static (bool Installed, bool Disabled) GetDriverState()
    {
        using var search = new ManagementObjectSearcher("SELECT PNPDeviceID, HardwareID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass='Display'");
        using var rows = search.Get();
        foreach (ManagementObject row in rows)
            using (row) if ((row["HardwareID"] as string[])?.Any(id => id.Equals(HardwareId, StringComparison.OrdinalIgnoreCase)) == true)
                return (true, Convert.ToUInt32(row["ConfigManagerErrorCode"]) == 22);
        return (false, false);
    }
    internal static int ActiveCount()
    {
        int count = 0;
        for (uint i = 0; i < 128; i++)
        {
            var adapter = Native.DisplayDevice.Create();
            if (!Native.EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.Flags & 1) != 0 && adapter.Description.Equals("Virtual Display Driver", StringComparison.OrdinalIgnoreCase)) count++;
        }
        return count;
    }
    internal static int? ParseConfiguredCount(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var root = XDocument.Load(reader).Root;
        return root?.Name == "vdd_settings" && int.TryParse(root.Element("monitors")?.Element("count")?.Value, out var count) && count >= 0 && count <= 256 ? count : null;
    }
    internal static int? ReadConfiguredCount()
    {
        try { return ParseConfiguredCount(File.ReadAllText(Path.Combine(ConfigurationFolder, "vdd_settings.xml"))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException) { return null; }
    }
    internal async Task<VirtualDisplayState> ReadAsync()
    {
        await gate.WaitAsync();
        try
        {
            var driver = await Task.Run(GetDriverState); bool installed = driver.Installed;
            int active = ActiveCount(); int? configured = ReadConfiguredCount();
            bool connected = false;
            if (installed)
            {
                try { using var timeout = new CancellationTokenSource(2000); connected = (await transport("PING", timeout.Token)).Contains("PONG", StringComparison.Ordinal); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException) { }
            }
            return new(installed, connected, active, configured, connected ? L.T("sf9d54b1977") : driver.Disabled ? L.T("s6477a5fda8") : installed ? L.T("s8bb9672434") : L.T("sb171d58330"), driver.Disabled);
        }
        finally { gate.Release(); }
    }
    internal static string CountCommand(int count)
    {
        if (count < 1 || count > MaximumDisplays) throw new ArgumentOutOfRangeException(nameof(count), L.T("saa537c558f"));
        return "SETDISPLAYCOUNT " + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    internal async Task SetCountAsync(int count)
    {
        if (count < 0 || count > MaximumDisplays) throw new ArgumentOutOfRangeException(nameof(count));
        await gate.WaitAsync();
        try
        {
            var driver = await Task.Run(GetDriverState);
            if (!driver.Installed) throw new InvalidOperationException(L.T("sac9043bf39"));
            if (count == 0)
            {
                // This upstream release clamps XML 0 to 1. Disable the real device
                // instead of pretending SETDISPLAYCOUNT 0 removed the last monitor.
                await DriverInstaller.SetEnabledAsync(false);
                for (int i = 0; i < 30; i++) { if (ActiveCount() == 0 && (await Task.Run(GetDriverState)).Disabled) return; await Task.Delay(250); }
                throw new InvalidOperationException(L.T("s9c573b7ce5"));
            }
            if (driver.Disabled) await DriverInstaller.SetEnabledAsync(true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            bool ready = false;
            for (int i = 0; i < 15; i++)
            {
                try { ready = (await transport("PING", timeout.Token)).Contains("PONG", StringComparison.Ordinal); } catch (Exception ex) when (ex is IOException or TimeoutException) { }
                if (ready) break; await Task.Delay(250, timeout.Token);
            }
            if (!ready) throw new InvalidOperationException(L.T("s3a8d153c7c"));
            string response = await transport(CountCommand(count), timeout.Token);
            if (response.Contains("Unknown command", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.T("sd9810dacd6"));
            // Commands restart the upstream adapter. A pipe write is not proof that
            // monitors appeared: verify persisted count AND actual desktop adapters.
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(350, timeout.Token);
                if (ReadConfiguredCount() == count && ActiveCount() == count) return;
            }
            throw new InvalidOperationException(L.F("s5c7e1b46e3", count, ActiveCount()));
        }
        finally { gate.Release(); }
    }
    internal static async Task<string> SendPipeAsync(string command, CancellationToken cancellation)
        => await SendPipeCoreAsync(command, cancellation, "MTTVirtualDisplayPipe");
    internal static async Task<string> SendPipeCoreAsync(string command, CancellationToken cancellation, string pipeName)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1500, cancellation);
        // Upstream 25.7.23 reads wchar_t commands, while PONG/log replies are UTF-8.
        byte[] bytes = Encoding.Unicode.GetBytes(command + "\0");
        await pipe.WriteAsync(bytes, cancellation); await pipe.FlushAsync(cancellation);
        using var result = new MemoryStream(); var buffer = new byte[4096];
        while (result.Length < 65536)
        {
            int read;
            try { read = await pipe.ReadAsync(buffer, cancellation); }
            catch (IOException) when (result.Length > 0) { break; }
            if (read == 0) break;
            result.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(result.ToArray()).Replace("\0", "");
    }
}
