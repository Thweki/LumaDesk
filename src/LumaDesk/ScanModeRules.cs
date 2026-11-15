// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace LumaDesk;

internal sealed record MonitorScanMode(uint Code)
{
    public override string ToString() => L.T(Code switch
    {
        0 => "scan.normal", 1 => "scan.underscan", 2 => "scan.overscan", 3 => "scan.widescreen", _ => "scan.unknown"
    });
}

internal sealed record MonitorScanModes(uint? Current, IReadOnlyList<MonitorScanMode> Choices);

internal static class ScanModeRules
{
    internal const string Flag = "LUMADESK_EXPERIMENTAL_OVERSCAN";
    internal const byte VcpCode = 0xDA;
    internal static bool Enabled => IsEnabled(Environment.GetEnvironmentVariable(Flag));
    internal static bool IsEnabled(string? value) => value == "1";

    internal static uint[] Parse(string capabilities)
    {
        if (capabilities.Length > 65536) return Array.Empty<uint>();
        string text = capabilities.Trim();
        int sectionDepth = text.StartsWith("(", StringComparison.Ordinal) ? 1 : 0;
        int depth = 0, start = -1, end = -1;
        // Only trust a top-level vcp section, never a model name or nested value list.
        for (int i = 0; i < text.Length; i++)
        {
            if (depth == sectionDepth && (i == 0 || text[i - 1] == '(' || char.IsWhiteSpace(text[i - 1])) &&
                text.AsSpan(i).StartsWith("vcp(", StringComparison.OrdinalIgnoreCase))
            {
                if (start >= 0) return Array.Empty<uint>();
                start = i + 4;
            }
            if (text[i] == '(') depth++;
            if (text[i] == ')')
            {
                depth--;
                if (depth < 0) return Array.Empty<uint>();
                if (start >= 0 && end < 0 && depth == sectionDepth) end = i;
            }
        }
        if (depth != 0 || start < 0 || end < start) return Array.Empty<uint>();
        string body = text.Substring(start, end - start).Trim();
        var features = Regex.Matches(body, @"\G\s*([0-9A-Fa-f]{2})(?:\s*\(([^()]*)\))?(?=\s|$)", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (features.Cast<Match>().Sum(f => f.Length) != body.Length) return Array.Empty<uint>();
        var scan = features.Cast<Match>().Where(f => f.Groups[1].Value.Equals("DA", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (scan.Length != 1) return Array.Empty<uint>();
        var values = scan[0].Groups[2].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (values.Any(v => v.Length != 2 || !byte.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
            return Array.Empty<uint>();
        // MCCS DA is discrete: 00 normal, 01 underscan, 02 overscan, 03 widescreen.
        // Unknown/vendor values are never offered or written. Zero is a valid choice.
        return values.Select(v => uint.Parse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .Where(v => v <= 3).Distinct().OrderBy(v => v).ToArray();
    }

    internal static MonitorScanModes Resolve(uint? current, string capabilities)
    {
        var codes = Parse(capabilities);
        if (!current.HasValue || !codes.Contains(current.Value)) return new(null, Array.Empty<MonitorScanMode>());
        return new(current, codes.Select(c => new MonitorScanMode(c)).ToArray());
    }

    internal static void RequireEnabled(bool enabled)
    {
        if (!enabled) throw new InvalidOperationException(L.T("scan.disabled"));
    }

    // Called under the display's DDC gate with freshly acquired, identity-checked handles.
    internal static MonitorScanModes Apply(bool enabled, uint target, Func<MonitorScanModes> inspect,
        Func<uint, bool> write, Func<uint?> read, Action? delay = null)
    {
        RequireEnabled(enabled);
        var state = inspect();
        if (target > 3 || !state.Current.HasValue || !state.Choices.Any(c => c.Code == target))
            throw new InvalidOperationException(L.T("scan.unavailable"));
        if (state.Current == target) return state;
        if (!write(target)) throw new InvalidOperationException(L.T("sea3ca6fb16"));
        delay ??= () => Thread.Sleep(200);
        int matching = 0;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            delay();
            matching = read() == target ? matching + 1 : 0;
            if (matching == 2) return state with { Current = target };
        }
        throw new InvalidOperationException(L.T("scan.unverified"));
    }
}
