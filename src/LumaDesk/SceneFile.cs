// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LumaDesk;

internal static class SceneFile
{
    internal const int MaxBytes = 1024 * 1024;
    private sealed class Document
    {
        public Document() { }
        [JsonRequired] public string Format { get; set; } = "LumaDesk.Scene";
        [JsonRequired] public int Version { get; set; } = 1;
        [JsonRequired] public string Name { get; set; } = "";
        [JsonRequired] public List<PresetDisplay> Displays { get; set; } = new();
    }

    internal static string Export(Preset scene)
    {
        var document = new Document { Name = scene.Name, Displays = scene.Displays };
        Validate(document);
        string json = JsonSerializer.Serialize(document, Storage.Json);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw InvalidFile();
        return json;
    }

    // Read at most one byte beyond the limit, even if a stream grows while being read.
    internal static Preset Import(Stream stream, IEnumerable<Preset> existing)
    {
        var bytes = new byte[MaxBytes + 1];
        int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > MaxBytes) throw InvalidFile();
        Document? document;
        try { document = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Storage.Json); }
        catch (JsonException ex) { throw new InvalidDataException(L.T("scene.fileInvalid"), ex); }
        Validate(document);
        string original = document!.Name.Trim(), name = original;
        var names = new HashSet<string>(existing.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = 2; names.Contains(name); i++)
        {
            string suffix = " (" + i + ")";
            name = original.Substring(0, Math.Min(original.Length, 40 - suffix.Length)) + suffix;
        }
        // A new identity and default triggers make repeated imports independent and inactive.
        return new Preset { Name = name, Displays = document.Displays };
    }

    private static InvalidDataException InvalidFile() => new(L.T("scene.fileInvalid"));

    private static void Validate(Document? document)
    {
        if (document == null || document.Format != "LumaDesk.Scene" || document.Version != 1 ||
            string.IsNullOrWhiteSpace(document.Name) || document.Name.Trim().Length > 40 ||
            document.Displays == null || document.Displays.Count == 0) throw InvalidFile();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in document.Displays)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Id) || !ids.Add(d.Id) ||
                d.Brightness is < 0 or > 100 || d.Contrast is < 0 or > 100 || d.Dimming is < 0 or > 85 ||
                d.Orientation is > 3 || d.Dpi is <= 0 or > 500) throw InvalidFile();
            if (d.Mode is { } mode && (mode.Width == 0 || mode.Width > int.MaxValue ||
                mode.Height == 0 || mode.Height > int.MaxValue || mode.Frequency == 0 ||
                (long)(d.X ?? 0) + mode.Width > int.MaxValue ||
                (long)(d.Y ?? 0) + mode.Height > int.MaxValue)) throw InvalidFile();
        }
    }
}
