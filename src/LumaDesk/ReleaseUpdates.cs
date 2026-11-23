// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDesk;

internal sealed record UpdateAsset(Version Version, string Name, string Hash)
{
    internal string Url => $"https://github.com/Thweki/LumaDesk/releases/download/v{Version}/{Name}";
}

internal static class ReleaseUpdates
{
    private static readonly HttpClient http = new() { DefaultRequestHeaders = { { "User-Agent", "LumaDesk/" + AppBrand.Version } } };

    // The key-path component in installer/Package.wxs stays the same across MSI upgrades.
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiLocateComponent(string component, StringBuilder path, ref int length);

    private static string PackageSuffix(string executable)
    {
        var path = new StringBuilder(32768);
        int length = path.Capacity;
        if (MsiLocateComponent("{A4E04935-C523-5ABE-B658-2DD48A3202CE}", path, ref length) == 3 &&
            string.Equals(path.ToString(), executable, StringComparison.OrdinalIgnoreCase))
            return "win-x64.msi";
        // The core library is bundled only in the standalone and portable editions.
#pragma warning disable IL3000
        return typeof(object).Assembly.Location.Length == 0 ? "win-x64.exe" : "slim-win-x64.exe";
#pragma warning restore IL3000
    }

    internal static UpdateAsset? SelectAsset(JsonElement releases, Version current, string suffix)
    {
        // Published previews use the same canonical vX.Y.Z tags as stable releases.
        Version? newest = null;
        JsonElement selected = default;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString();
            if (tag == null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
                version.Build < 0 || version.Revision >= 0 || tag != "v" + version || version <= current || version <= newest) continue;
            newest = version;
            selected = release;
        }
        if (newest == null) return null;
        var name = $"LumaDesk-{newest}-{suffix}";
        foreach (var asset in selected.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            var digest = asset.GetProperty("digest").GetString();
            if (digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.Ordinal) && digest[7..].All(Uri.IsHexDigit))
                return new(newest, name, digest[7..]);
        }
        throw new InvalidDataException("Release is missing a verified " + name);
    }

    internal static async Task<PendingUpdate?> DownloadAsync(CancellationToken cancellation)
    {
        string executable = Environment.ProcessPath!;
        if (FileVersionInfo.GetVersionInfo(executable).ProductName != AppBrand.Name)
            throw new InvalidOperationException("Automatic installation requires the LumaDesk executable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        const string feed = "https://api.github.com/repos/Thweki/LumaDesk/releases?per_page=30";
        using var feedStream = await http.GetStreamAsync(feed, token);
        using var releases = await JsonDocument.ParseAsync(feedStream, cancellationToken: token);
        var asset = SelectAsset(releases.RootElement, Version.Parse(AppBrand.Version), PackageSuffix(executable));
        if (asset == null) return null;
        bool msi = asset.Name.EndsWith(".msi", StringComparison.Ordinal);
        // File.Replace needs the portable download and destination on the same volume.
        string directory = msi ? Path.GetTempPath() : Path.GetDirectoryName(executable)!;
        string path = Path.Combine(directory, $"LumaDesk-{Guid.NewGuid():N}" + (msi ? ".msi" : ".pending"));
        var pending = new PendingUpdate(asset.Version, path, executable, asset.Hash);
        try
        {
            using var download = await http.GetStreamAsync(asset.Url, token);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true);
            await download.CopyToAsync(file, token);
            file.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!hash.Equals(asset.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded update failed SHA-256 verification.");
            return pending;
        }
        catch { pending.Discard(); throw; }
    }
}

internal sealed record PendingUpdate(Version Version, string PackagePath, string Executable, string Hash)
{
    internal void Discard()
    {
        try { File.Delete(PackagePath); } catch (Exception ex) { Storage.Log("Update cleanup: " + ex.Message); }
    }

    internal ProcessStartInfo InstallerCommand(int parentId)
    {
        using var script = new StreamReader(typeof(PendingUpdate).Assembly.GetManifestResourceStream("LumaDesk.ApplyUpdate.ps1")!);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script.ReadToEnd());
        start.Environment["LUMADESK_UPDATE_PARENT"] = parentId.ToString();
        start.Environment["LUMADESK_UPDATE_PACKAGE"] = PackagePath;
        start.Environment["LUMADESK_UPDATE_TARGET"] = Executable;
        start.Environment["LUMADESK_UPDATE_FROM"] = AppBrand.Version;
        start.Environment["LUMADESK_UPDATE_HASH"] = Hash;
        start.Environment["LUMADESK_UPDATE_LOG"] = Path.Combine(Storage.Folder, "app.log");
        return start;
    }

    internal void InstallAfterExit()
    {
        using var process = Process.Start(InstallerCommand(Environment.ProcessId)) ?? throw new IOException("Could not start the installer.");
    }
}
