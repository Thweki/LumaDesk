// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using LumaDesk;

internal static class ReleaseUpdateTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var hash = Convert.ToHexString(SHA256.HashData("test update"u8));
        object Release(string version, bool draft = false) => new
        {
            draft, prerelease = true, tag_name = "v" + version,
            assets = new[] { "slim-win-x64.exe", "win-x64.msi", "win-x64.exe" }.Select(suffix =>
                new { name = $"LumaDesk-{version}-{suffix}", digest = "sha256:" + hash })
        };
        var releases = JsonSerializer.SerializeToElement(new[] { Release("1.0.0"), Release("3.0.0", true), Release("2.0.0"), Release("2.1.0-beta") });
        var current = new Version(1, 0, 0);
        foreach (var suffix in new[] { "slim-win-x64.exe", "win-x64.exe", "win-x64.msi" })
        {
            var asset = ReleaseUpdates.SelectAsset(releases, current, suffix);
            check(asset?.Name == "LumaDesk-2.0.0-" + suffix && asset.Hash == hash, "Select newest public release in the installed edition: " + suffix);
        }
        check(ReleaseUpdates.SelectAsset(releases, new Version(2, 0, 0), "win-x64.exe") == null, "Skip equal and older updates");
        check(!JsonSerializer.Deserialize<Preferences>("{}")!.AutoUpdate, "Existing profiles default automatic updates off");

        Directory.CreateDirectory(Storage.Folder);
        var target = Path.Combine(Storage.Folder, "portable [test] with spaces.exe");
        var package = Path.Combine(Storage.Folder, "staged [test].pending");
        var original = File.ReadAllBytes(typeof(PendingUpdate).Assembly.Location);
        File.WriteAllBytes(target, original);
        foreach (var valid in new[] { false, true })
        {
            File.WriteAllText(package, "test update");
            var update = new PendingUpdate(current, package, target, valid ? hash : new string('0', 64));
            using var installer = Process.Start(update.InstallerCommand(int.MaxValue))!;
            await installer.WaitForExitAsync();
            if (valid && installer.ExitCode != 0) throw new IOException(File.ReadAllText(Path.Combine(Storage.Folder, "app.log")));
            bool expectedContent = valid ? File.ReadAllText(target) == "test update" : File.ReadAllBytes(target).SequenceEqual(original);
            check((installer.ExitCode == 0) == valid && expectedContent && !File.Exists(package),
                valid ? "Verified portable update replaces the original path after exit" : "Corrupt package leaves the current copy intact");
        }
    }
}
