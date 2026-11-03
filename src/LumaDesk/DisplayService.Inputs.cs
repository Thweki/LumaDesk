// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace LumaDesk;

internal sealed record MonitorInput(uint Code, uint? WriteCode = null)
{
    internal uint TransportCode => WriteCode ?? Code;
    public override string ToString() => Code switch { 1 => "VGA 1", 2 => "VGA 2", 3 => "DVI 1", 4 => "DVI 2", 15 => "DisplayPort 1", 16 => "DisplayPort 2", 17 => "HDMI 1", 18 => "HDMI 2", _ => L.F("input.number", Code) };
}
internal sealed record MonitorInputs(uint? Current, IReadOnlyList<MonitorInput> Choices, uint? RawCurrent = null, bool LegacyCodes = false);
internal sealed record InputSwitchResult(bool Confirmed, uint? Current);

internal sealed partial class DisplayService
{
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
    [DllImport("dxva2.dll", CharSet = CharSet.Ansi, SetLastError = true)] private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder capabilities, uint length);
    private readonly ConcurrentDictionary<string, (DateTime Time, uint[] Codes)> inputCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> legacyInputCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> inputWriteGates = new(StringComparer.OrdinalIgnoreCase);
    private static bool WithDisplay(DisplayInfo display, Func<IntPtr, bool> operation)
    {
        ValidateIdentity(display.Device, display.Id);
        bool result = false;
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Native.Rect rect, IntPtr data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info) || info.Device != display.Device) return true;
            result = WithPhysical(monitor, operation); return false;
        }, IntPtr.Zero);
        return result;
    }
    internal Task<MonitorInputs> ReadInputsAsync(DisplayInfo display) => Task.Run(() =>
    {
        lock (Gate(display))
        {
            uint? current = null; uint[] codes = Array.Empty<uint>();
            WithDisplay(display, handle =>
            {
                if (Native.GetVCPFeatureAndVCPFeatureReply(handle, 0x60, out _, out uint value, out _)) current = value;
                if (inputCache.TryGetValue(display.Id, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(5)) codes = cached.Codes;
                else if (GetCapabilitiesStringLength(handle, out uint length) && length > 0 && length <= 65536)
                {
                    var text = new StringBuilder((int)length);
                    if (CapabilitiesRequestAndCapabilitiesReply(handle, text, length))
                    {
                        codes = InputRules.Parse(text.ToString());
                        inputCache[display.Id] = (DateTime.UtcNow, codes);
                    }
                }
                return true;
            });
            legacyInputCodes.TryGetValue(display.Id, out bool previousLegacy);
            var inputs = InputRules.Resolve(current, codes, previousLegacy);
            if (current.HasValue) legacyInputCodes[display.Id] = inputs.LegacyCodes;
            return inputs;
        }
    });
    internal async Task<InputSwitchResult> SetInputAsync(DisplayInfo display, uint code)
    {
        var serial = inputWriteGates.GetOrAdd(display.Id, _ => new SemaphoreSlim(1, 1));
        await serial.WaitAsync();
        Interlocked.Increment(ref activeWrites);
        try
        {
            var inputs = await ReadInputsAsync(display);
            var choice = inputs.Choices.FirstOrDefault(i => i.Code == code) ?? throw new InvalidOperationException(L.T("scene.unsupported"));
            if (inputs.Current == code) return new(true, code);
            Storage.Log($"Input {display.Name}: current={inputs.RawCurrent:X2}, target={code:X2}, write={choice.TransportCode:X2}, legacy={inputs.LegacyCodes}");
            var result = await InputSwitch.VerifyAsync(choice.TransportCode, () => Task.Run(() =>
            {
                lock (Gate(display))
                    if (!WithDisplay(display, h => Native.SetVCPFeature(h, 0x60, choice.TransportCode)))
                        throw new InvalidOperationException(L.T("sea3ca6fb16"));
            }), () => ReadCurrentInputAsync(display));
            Storage.Log($"Input {display.Name}: confirmed={result.Confirmed}, actual={result.Current:X2}");
            return result with { Current = inputs.Choices.FirstOrDefault(i => i.TransportCode == result.Current)?.Code };
        }
        finally { Interlocked.Decrement(ref activeWrites); serial.Release(); }
    }

    private Task<uint?> ReadCurrentInputAsync(DisplayInfo display) => Task.Run(() =>
    {
        lock (Gate(display))
        {
            uint? current = null;
            try
            {
                WithDisplay(display, handle =>
                {
                    if (Native.GetVCPFeatureAndVCPFeatureReply(handle, 0x60, out _, out uint value, out _)) current = value;
                    return true;
                });
            }
            // Losing the DDC route is expected when switching away from this PC.
            catch (InvalidOperationException) { }
            return current;
        }
    });
}

internal static class InputSwitch
{
    internal static async Task<InputSwitchResult> VerifyAsync(uint target, Func<Task> write, Func<Task<uint?>> read, Func<Task>? delay = null)
    {
        await write(); // Never retry an input write: the connection may have moved.
        delay ??= () => Task.Delay(300);
        uint? actual = null; int matching = 0;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await delay(); actual = await read();
            matching = actual == target ? matching + 1 : 0;
            if (matching >= 2) return new(true, target);
        }
        if (actual.HasValue && actual != target)
            throw new InvalidOperationException(L.T("input.unchanged"));
        return new(false, actual);
    }
}

internal static class InputRules
{
    // Some firmware advertises MCCS 0F/10/11/12 but actually uses 07/08/05/06.
    // Evidence: https://github.com/rockowitz/ddcutil/issues/157
    // Gate this quirk on BOTH the digital-only capability set and an observed
    // undocumented current value. Never probe unknown inputs by writing guesses.
    internal static MonitorInputs Resolve(uint? raw, IReadOnlyList<uint> codes, bool previousLegacy = false)
    {
        static uint Legacy(uint code) => code switch { 0x11 => 0x05, 0x12 => 0x06, 0x0F => 0x07, 0x10 => 0x08, _ => code };
        bool digitalOnly = codes.Count > 0 && codes.All(c => c is 0x0F or 0x10 or 0x11 or 0x12);
        bool legacy = digitalOnly && (raw.HasValue ? !codes.Contains(raw.Value) && codes.Any(c => Legacy(c) == raw) : previousLegacy);
        var choices = codes.Distinct().Select(c => new MonitorInput(c, legacy ? Legacy(c) : c)).ToArray();
        uint? current = choices.FirstOrDefault(c => c.TransportCode == raw)?.Code;
        return new(current, choices, raw, legacy);
    }

    internal static uint[] Parse(string capabilities)
    {
        if (capabilities.Length > 65536) return Array.Empty<uint>();
        // Restrict parsing to the vcp section; model names and other fields can contain hex.
        int start = capabilities.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return Array.Empty<uint>();
        int depth = 1, end = start + 4;
        for (; end < capabilities.Length && depth > 0; end++) { if (capabilities[end] == '(') depth++; if (capabilities[end] == ')') depth--; }
        if (depth != 0) return Array.Empty<uint>();
        string body = capabilities.Substring(start + 4, end - start - 5);
        foreach (Match feature in Regex.Matches(body, @"(?:^|\s)([0-9A-Fa-f]{2})(?:\s*\(([^()]*)\))?(?=\s|$)", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            if (!feature.Groups[1].Value.Equals("60", StringComparison.OrdinalIgnoreCase)) continue;
            var values = feature.Groups[2].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Any(v => !uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) || code == 0 || code > 255)) return Array.Empty<uint>();
            return values.Select(v => uint.Parse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).Distinct().ToArray();
        }
        return Array.Empty<uint>();
    }
}
