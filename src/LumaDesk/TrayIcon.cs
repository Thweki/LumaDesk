// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using Microsoft.Win32;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace LumaDesk;

internal static class TrayIcon
{
    internal static bool UseLightGlyph()
    {
        if (SystemParameters.HighContrast)
        {
            var color = System.Windows.SystemColors.WindowTextColor;
            return color.R + color.G + color.B > 384;
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
        }
        catch { return true; }
    }

    public static Icon Create(bool? light = null)
    {
        using var bitmap = Render(32, light ?? UseLightGlyph());
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    internal static Bitmap Render(int size, bool light)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("LumaDesk.Assets.logo.png")
            ?? throw new InvalidDataException("Missing LumaDesk logo resource.");
        using var master = new Bitmap(stream);
        // Normalize transparent margins for readable 16–48 px icons. The approved
        // silhouette and alpha are preserved; only the output size and ink change.
        int left = master.Width, top = master.Height, right = 0, bottom = 0;
        for (int y = 0; y < master.Height; y += 4)
            for (int x = 0; x < master.Width; x += 4)
                if (master.GetPixel(x, y).A >= 128)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        float side = Math.Max(right - left, bottom - top) * 1.12f;
        var source = new RectangleF((left + right - side) / 2, (top + bottom - side) / 2, side, side);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attributes = new ImageAttributes();
        float ink = light ? 1f : 0.125f;
        attributes.SetColorMatrix(new ColorMatrix(new[]
        {
            new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 1, 0 },
            new float[] { ink, ink, ink, 0, 1 }
        }));
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(master, new Rectangle(0, 0, size, size), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }
}
