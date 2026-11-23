// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace LumaDesk;

internal static class LogExport
{
    private const uint GenericRead = 0x80000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint DiskFile = 1;
    private const int FileNotFound = 2, PathNotFound = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public FileAttributes Attributes;
        public FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share,
        IntPtr security, FileMode disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle file);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    internal static bool IsOrdinaryLog(bool disk, FileAttributes attributes, uint links) =>
        disk && links == 1 &&
        (attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) == 0;

    public static int Run(string[] args, string folder)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 2 || args[0] != "--export-log" ||
            string.IsNullOrWhiteSpace(args[1])) return 1;
        try { Export(folder, args[1]); return 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Export failures must not append to or rotate the logs being collected.
            return 1;
        }
    }

    private static FileStream? OpenLog(string path)
    {
        // Inspect the actual file object, without following its final path component.
        // Keeping this handle open permits ordinary appends and rotation of the log.
        var handle = CreateFileW(NativePath(path), GenericRead, FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero, FileMode.Open, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is FileNotFound or PathNotFound) return null;
            throw new IOException("Could not open the diagnostic log.", new Win32Exception(error));
        }

        try
        {
            bool disk = GetFileType(handle) == DiskFile;
            if (!disk || !GetFileInformationByHandle(handle, out var information) ||
                !IsOrdinaryLog(disk, information.Attributes, information.NumberOfLinks))
                throw new IOException("Only regular, single-linked diagnostic logs can be exported.");
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static string NativePath(string path)
    {
        // FileStream adds an extended-length prefix when needed; the native open does not.
        if (path.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    private static void Export(string folder, string destination)
    {
        string output = Path.GetFullPath(destination);
        var sources = new List<(string Name, FileStream Stream)>();
        string? staging = null;
        try
        {
            foreach (string name in new[] { "app.previous.log", "app.log" })
            {
                string path = Path.GetFullPath(Path.Combine(folder, name));
                if (string.Equals(output, path, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The export destination cannot be a source log.");
                if (OpenLog(path) is { } source) sources.Add((name, source));
            }
            if (sources.Count == 0) throw new FileNotFoundException("No diagnostic logs are available.");

            // Write and close a complete ZIP before giving it the requested name.
            string temporary = Path.Combine(Path.GetDirectoryName(output) ?? throw new IOException("Missing destination directory."),
                $".lumadesk-export-{Guid.NewGuid():N}.tmp");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                staging = temporary;
                using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var source in sources)
                {
                    using var entry = archive.CreateEntry(source.Name).Open();
                    source.Stream.CopyTo(entry);
                }
            }
            File.Move(temporary, output, overwrite: false);
            staging = null;
        }
        finally
        {
            // Best-effort cleanup of this invocation's staging file, never the requested path.
            if (staging != null)
                try { File.Delete(staging); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            foreach (var source in sources) source.Stream.Dispose();
        }
    }
}
