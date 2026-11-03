// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using LumaDesk;

var path = Path.GetFullPath(args.Length > 0 ? args[0] : "src/LumaDesk/Assets/app.ico");
var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
using (var brand = TrayIcon.Render(256, false)) brand.Save(Path.Combine(Path.GetDirectoryName(path)!, "brand.png"), ImageFormat.Png);
var frames = new List<byte[]>();
foreach (int size in sizes)
{
    using var bitmap = TrayIcon.Render(size, false);
    using var buffer = new MemoryStream();
    bitmap.Save(buffer, ImageFormat.Png); frames.Add(buffer.ToArray());
}
using var writer = new BinaryWriter(File.Create(path));
writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
int offset = 6 + sizes.Length * 16;
for (int i = 0; i < sizes.Length; i++)
{
    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
    writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
}
foreach (var frame in frames) writer.Write(frame);
Console.WriteLine("Created 9-resolution ICO: " + path);
