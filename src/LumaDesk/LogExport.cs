// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace LumaDesk;

internal static class LogExport
{
    public static int Run(string[] args, string folder)
    {
        if (args.Length != 2 || args[0] != "--export-log" || string.IsNullOrWhiteSpace(args[1])) return 1;
        try { Export(folder, args[1]); return 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Export failures must not append to or rotate the logs being collected.
            return 1;
        }
    }

    private static void Export(string folder, string destination)
    {
        string output = Path.GetFullPath(destination);
        var sources = new List<(string Name, FileStream Stream)>();
        bool created = false;
        try
        {
            foreach (string name in new[] { "app.previous.log", "app.log" })
            {
                string path = Path.GetFullPath(Path.Combine(folder, name));
                if (string.Equals(output, path, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The export destination cannot be a source log.");
                try
                {
                    sources.Add((name, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)));
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            if (sources.Count == 0) throw new FileNotFoundException("No diagnostic logs are available.");

            // CreateNew also protects existing settings, logs and previous exports.
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            foreach (var source in sources)
            {
                using var entry = archive.CreateEntry(source.Name).Open();
                source.Stream.CopyTo(entry);
            }
        }
        catch
        {
            if (created) File.Delete(output);
            throw;
        }
        finally
        {
            foreach (var source in sources) source.Stream.Dispose();
        }
    }
}
