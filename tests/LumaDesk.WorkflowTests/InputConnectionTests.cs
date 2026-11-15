// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class InputConnectionTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var (technology, label) in new (uint, string)[]
        {
            (0, "VGA"), (4, "DVI"), (5, "HDMI"), (6, "LVDS"),
            (10, "DisplayPort"), (11, "eDP"), (15, "Miracast"),
            (0x80000000, L.T("input.connectionInternal"))
        })
            check(InputRules.ConnectionLabel(technology) == label, "Windows connector label: " + label);

        foreach (uint? unknown in new uint?[] { null, uint.MaxValue, 7, 17, 99 })
            check(InputRules.ConnectionLabel(unknown) == null, "Missing or unrecognized Windows connectors do not invent a label");
        check(InputRules.ConnectionLabel(15) == "Miracast" && new MonitorInput(15).ToString() == "DisplayPort 1",
            "Windows connector codes and DDC input codes remain separate namespaces");

        var unreadable = InputRules.Resolve(null, Array.Empty<uint>()) with { ConnectionTechnology = 5 };
        check(unreadable.Current == null && unreadable.RawCurrent == null && unreadable.Choices.Count == 0 && !unreadable.LegacyCodes,
            "An OS connector never manufactures a confirmed current input or a switchable choice");
        var legacy = InputRules.Resolve(7, new uint[] { 17, 18, 15 }) with { ConnectionTechnology = 10 };
        check(legacy.Current == 15 && legacy.RawCurrent == 7 && legacy.LegacyCodes &&
            legacy.Choices.Single(i => i.Code == 15).TransportCode == 7,
            "Connector metadata preserves firmware compatibility readback and write codes");
    }
}
