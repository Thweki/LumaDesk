// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;

internal static class ScanModeTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (string? value in new string?[] { null, "", "0", "true", "yes", " 1", "1 " })
            check(!ScanModeRules.IsEnabled(value), "Scan mode experiment defaults off: " + (value ?? "unset"));
        check(ScanModeRules.IsEnabled("1"), "Scan mode experiment requires explicit opt-in");
        check(ScanModeRules.VcpCode == 0xDA, "Scan mode uses discrete MCCS DA, not scaling or contrast");
        check(ScanModeRules.Parse("(prot(monitor) model(DA) vcp(10 60(11 12) dA(02 00 01 03 01 FF)))")
            .SequenceEqual(new uint[] { 0, 1, 2, 3 }), "Parse scan modes including normal zero; deduplicate and omit vendor values");
        check(ScanModeRules.Parse("vcp(DA(00 02))").SequenceEqual(new uint[] { 0, 2 }), "Unwrapped capability section is supported");
        foreach (string text in new[]
        {
            "", "(vcp(10 60(00 01 02)))", "(model(vcp(DA(00 01))))", "(vcp(DA))", "(vcp(DA()))",
            "(vcp(DA(00 01))", "(vcp(DA(00 01))))", "(vcp(60(DA(00 01))))", "(vcp(DA(00 01) junk))",
            "(vcp(DA(00 ZZ)))", "(vcp(DA(00 100)))", "(vcp(DA(0 1)))", "(vcp(DA(00 01) DA(00 02)))",
            "(vcp(DA(00 01)) vcp(DA(00 02)))", "(vcp(DA(FF)))", new string(' ', 65537)
        }) check(ScanModeRules.Parse(text).Length == 0, "Malformed, absent, ambiguous or unknown scan capability is rejected: " + text[..Math.Min(70, text.Length)]);
        check(ScanModeRules.Resolve(2, "(vcp(DA(00 02)))").Current == 2, "Readable advertised current scan mode is retained");
        foreach (uint? current in new uint?[] { null, 1, 255, 0x100 })
            check(ScanModeRules.Resolve(current, "(vcp(DA(00 02)))").Choices.Count == 0,
                "Unknown, unreadable or unadvertised current scan mode cannot be changed");

        int inspections = 0, writes = 0, reads = 0;
        uint written = uint.MaxValue;
        var state = ScanModeRules.Resolve(2, "(vcp(DA(00 01 02)))");
        MonitorScanModes Inspect() { inspections++; return state; }
        bool Write(uint code) { writes++; written = code; return true; }
        uint? Read() { reads++; return written; }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, message);
        }
        Reject(() => ScanModeRules.Apply(false, 0, Inspect, Write, Read, () => { }), "Disabled scan mode write is rejected");
        check(inspections == 0 && writes == 0 && reads == 0, "Disabled experiment performs no hardware I/O");
        foreach (uint code in new uint[] { 3, 4, uint.MaxValue })
            Reject(() => ScanModeRules.Apply(true, code, Inspect, Write, Read, () => { }), "Unadvertised or invalid scan target rejected");
        check(writes == 0 && reads == 0, "Rejected targets never write or verify");
        ScanModeRules.Apply(true, 2, Inspect, Write, Read, () => { });
        check(writes == 0 && reads == 0, "Applying current scan mode performs no write");
        var result = ScanModeRules.Apply(true, 0, Inspect, Write, Read, () => { });
        check(result.Current == 0 && written == 0 && writes == 1 && reads == 2,
            "Normal mode writes raw zero once and requires two matching readbacks");
        state = ScanModeRules.Resolve(0, "(vcp(DA(00 02)))");
        Reject(() => ScanModeRules.Apply(true, 1, Inspect, Write, Read, () => { }), "Fresh capabilities reject a formerly offered mode");
        check(writes == 1, "Changed capabilities cannot trigger stale selection writes");
        state = new(null, Array.Empty<MonitorScanMode>());
        Reject(() => ScanModeRules.Apply(true, 0, Inspect, Write, Read, () => { }), "Unreadable monitor rejects writes");
        state = ScanModeRules.Resolve(0, "(vcp(DA(00 01 02)))");
        reads = 0;
        Reject(() => ScanModeRules.Apply(true, 1, Inspect, _ => false, Read, () => { }), "Native write failure is reported");
        check(reads == 0, "Failed writes are not reported as verified");
        foreach (uint? actual in new uint?[] { null, 0, 255 })
        {
            int before = writes; reads = 0;
            Reject(() => ScanModeRules.Apply(true, 1, Inspect, Write, () => { reads++; return actual; }, () => { }),
                "Unreadable, ignored or unknown readback never reports success");
            check(writes == before + 1 && reads == 5, "Verification is bounded and never retries a write");
        }
        var sequence = new Queue<uint?>(new uint?[] { 1, null, 0, 1, 1 });
        result = ScanModeRules.Apply(true, 1, Inspect, Write, () => sequence.Dequeue(), () => { });
        check(result.Current == 1 && sequence.Count == 0, "Transient readbacks require consecutive matches");
        int beforeDisconnect = writes;
        Reject(() => ScanModeRules.Apply(true, 1, () => throw new InvalidOperationException("Disconnected"), Write, Read, () => { }),
            "Disconnect during fresh inspection propagates");
        check(writes == beforeDisconnect, "Disconnect before validation prevents writes");
    }
}
