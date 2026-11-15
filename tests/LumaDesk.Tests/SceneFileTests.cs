// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LumaDesk;

internal static class SceneFileTests
{
    internal static void Run(Action<bool, string> check)
    {
        Preset Import(string json, params Preset[] existing)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return SceneFile.Import(stream, existing);
        }
        var source = new Preset
        {
            Name = "夜間", OnConnect = true, AtTime = "22:00", OnPower = "battery",
            Shortcut = new() { Key = 65 },
            Displays = new()
            {
                new() { Id = "DISPLAY\\A", Name = "Main", Brightness = 42, Dimming = 15,
                    Mode = new(2560, 1440, 144), Orientation = 0, Dpi = 150, Hdr = false,
                    UserProfiles = true, SdrProfile = "screen.icm", HdrProfile = "", X = 0, Y = 0, Primary = true },
                new() { Id = "DISPLAY\\B", Name = "Side", Brightness = null, Dimming = 30 }
            }
        };
        string json = SceneFile.Export(source);
        var imported = Import(json);
        check(imported.Name == source.Name && JsonSerializer.Serialize(imported.Displays) == JsonSerializer.Serialize(source.Displays),
            "Scene file roundtrip preserves all display fields, Unicode and unspecified capabilities");
        check(imported.Id != source.Id && imported.Id != Import(json).Id,
            "Each scene import receives an independent identity");
        check(!imported.OnConnect && imported.AtTime == null && imported.OnPower == null && imported.Shortcut == null,
            "Imported scenes have no automatic triggers or shortcuts");
        var root = JsonNode.Parse(json)!.AsObject();
        check(new[] { "Id", "OnConnect", "AtTime", "OnPower", "Shortcut" }.All(k => !root.ContainsKey(k)),
            "Export excludes local scene identity and automation");
        root["OnConnect"] = true; root["AtTime"] = "12:00"; root["OnPower"] = "ac";
        root["Shortcut"] = JsonSerializer.SerializeToNode(source.Shortcut); root["Id"] = source.Id;
        var injected = Import(root.ToJsonString());
        check(!injected.OnConnect && injected.AtTime == null && injected.OnPower == null && injected.Shortcut == null && injected.Id != source.Id,
            "Extra automation fields cannot activate an imported scene");
        imported.Displays[0].Brightness = 9;
        check(source.Displays[0].Brightness == 42 && source.OnConnect && source.Shortcut?.Key == 65,
            "Import and export leave the source preset unchanged");
        check(Import(json, source, new Preset { Name = "夜間 (2)" }).Name == "夜間 (3)",
            "Repeated imports choose a free name without replacing existing scenes");
        source.Name = new string('A', 40);
        var longName = Import(SceneFile.Export(source), new Preset { Name = source.Name.ToLowerInvariant() });
        check(longName.Name == new string('A', 36) + " (2)",
            "Name conflicts ignore case and keep suffixed names within the editor limit");

        void Reject(string input, string reason)
        {
            bool rejected = false;
            try { Import(input); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Scene import rejects " + reason);
        }
        Reject("{", "malformed JSON"); Reject("null", "null document"); Reject("{}", "missing envelope");
        Reject(new string(' ', SceneFile.MaxBytes + 1), "oversized input");
        check(Import(json + new string(' ', SceneFile.MaxBytes - Encoding.UTF8.GetByteCount(json))).Displays.Count == 2,
            "The documented file size limit is inclusive");
        foreach (var (key, value) in new (string, JsonNode?)[]
        {
            ("Version", JsonValue.Create(2)), ("Format", JsonValue.Create("settings")),
            ("Name", JsonValue.Create(" ")), ("Name", JsonValue.Create(new string('x', 41))),
            ("Displays", null), ("Displays", new JsonArray()), ("Displays", new JsonArray((JsonNode?)null))
        })
        {
            var invalid = JsonNode.Parse(json)!; invalid[key] = value;
            Reject(invalid.ToJsonString(), "invalid " + key);
        }
        foreach (string key in new[] { "Format", "Version", "Name", "Displays" })
        {
            var invalid = JsonNode.Parse(json)!.AsObject(); invalid.Remove(key);
            Reject(invalid.ToJsonString(), "missing " + key);
        }
        foreach (var (key, value) in new (string, JsonNode?)[]
        {
            ("Id", null), ("Id", JsonValue.Create(" ")), ("Brightness", JsonValue.Create(-1)),
            ("Brightness", JsonValue.Create(101)), ("Dimming", JsonValue.Create(86)),
            ("Dimming", JsonValue.Create(-1)), ("Orientation", JsonValue.Create(4)),
            ("Dpi", JsonValue.Create(0)), ("Mode", JsonNode.Parse("{\"Width\":0,\"Height\":1080,\"Frequency\":60}")),
            ("X", JsonValue.Create(int.MaxValue))
        })
        {
            var invalid = JsonNode.Parse(json)!; invalid["Displays"]![0]![key] = value;
            Reject(invalid.ToJsonString(), "invalid display " + key);
        }
        var duplicate = JsonNode.Parse(json)!;
        duplicate["Displays"]![1]!["Id"] = "display\\a";
        Reject(duplicate.ToJsonString(), "case-insensitive duplicate display IDs");
        check(Import(json).Displays.Count == 2, "A valid import still succeeds after rejected files");
        source.Displays[0].Brightness = 101;
        bool exportRejected = false;
        try { SceneFile.Export(source); } catch (InvalidDataException) { exportRejected = true; }
        check(exportRejected, "Export validates settings before creating a file");
    }
}
