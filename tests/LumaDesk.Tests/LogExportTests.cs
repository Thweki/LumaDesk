// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.IO.Compression;
using System.Text;
using LumaDesk;

internal static class LogExportTests
{
    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "lumadesk-log-export-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        string output = Path.Combine(root, "support logs.zip");
        Directory.CreateDirectory(root);
        int Export(string path) => LogExport.Run(new[] { "--export-log", path }, data);
        try
        {
            check(Export(output) == 1 && !Directory.Exists(data) && !File.Exists(output),
                "Log export with no data directory fails without creating files");
            Directory.CreateDirectory(data);
            check(Export(output) == 1 && !File.Exists(output), "Log export with no logs produces no archive");
            string current = Path.Combine(data, "app.log");
            string previous = Path.Combine(data, "app.previous.log");
            byte[] recent = Encoding.UTF8.GetBytes("Display scan: 1234 ms\r\n屏幕: probe failed\n");
            byte[] older = Encoding.UTF8.GetBytes("Previous session\n");
            File.WriteAllBytes(current, recent);
            File.WriteAllBytes(previous, older);
            File.WriteAllText(Path.Combine(data, "settings.json"), "private settings fixture");
            File.WriteAllText(Path.Combine(data, "mode-recovery.json"), "recovery fixture");
            File.WriteAllText(Path.Combine(data, "unrelated.log"), "unrelated fixture");

            using (var writer = new FileStream(current, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                check(Export(output) == 0, "Log export succeeds while the current log is open for writing");
            }
            using (var archive = ZipFile.OpenRead(output))
            {
                check(archive.Entries.Select(e => e.FullName).OrderBy(n => n).SequenceEqual(new[] { "app.log", "app.previous.log" }),
                    "Log archive contains only current and rotated logs with relative names");
                check(Read(archive, "app.log").SequenceEqual(recent) && Read(archive, "app.previous.log").SequenceEqual(older),
                    "Log export preserves exact bytes including Unicode and line endings");
            }
            byte[] existing = File.ReadAllBytes(output);
            check(Export(output) == 1 && File.ReadAllBytes(output).SequenceEqual(existing),
                "Log export refuses to overwrite an existing export");
            foreach (string protectedFile in new[] { current, previous, Path.Combine(data, "settings.json") })
            {
                byte[] before = File.ReadAllBytes(protectedFile);
                check(Export(protectedFile) == 1 && File.ReadAllBytes(protectedFile).SequenceEqual(before),
                    "Log export preserves existing data: " + Path.GetFileName(protectedFile));
            }
            File.Delete(output);
            foreach (string[] args in new[] {
                Array.Empty<string>(), new[] { "--export-log" }, new[] { "--export-log", " " },
                new[] { "--export-log", output, "extra" }, new[] { "--wrong-command", output }
            })
                check(LogExport.Run(args, data) == 1 && !File.Exists(output), "Invalid log export arguments fail without output");
            check(Export(Path.Combine(root, "missing", "logs.zip")) == 1, "Log export reports an unavailable output directory");
            check(File.ReadAllBytes(current).SequenceEqual(recent) && File.ReadAllBytes(previous).SequenceEqual(older),
                "Successful and failed exports never append to or rotate source logs");

            File.Delete(previous);
            check(Export(previous) == 1 && !File.Exists(previous), "Log export cannot create an archive at a missing rotated log path");
            check(Export(output) == 0, "Log export works with only the current log");
            using (var archive = ZipFile.OpenRead(output))
                check(archive.Entries.Count == 1 && Read(archive, "app.log").SequenceEqual(recent), "Current-only export contains its log");
            File.Delete(output);
            File.Delete(current);
            File.WriteAllBytes(previous, older);
            check(Export(current) == 1 && !File.Exists(current), "Log export cannot create an archive at a missing current log path");
            check(Export(output) == 0, "Log export works with only the rotated log");
            using (var archive = ZipFile.OpenRead(output))
                check(archive.Entries.Count == 1 && Read(archive, "app.previous.log").SequenceEqual(older), "Rotated-only export contains its log");
            File.Delete(output);
            File.Delete(previous);
            File.WriteAllBytes(current, Array.Empty<byte>());
            check(Export(output) == 0, "An existing empty log can be exported");
            using (var archive = ZipFile.OpenRead(output))
                check(archive.Entries.Count == 1 && Read(archive, "app.log").Length == 0, "Empty logs remain empty in the archive");
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] Read(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
