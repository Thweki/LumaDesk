// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Drawing;
using LumaDesk;

internal static class PipBufferTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Only tiny in-memory bitmaps; no window, screen capture or display writes.
        Bitmap? buffer = null;
        try
        {
            var first = PipWindow.EnsureBuffer(ref buffer, 2, 3);
            check(ReferenceEquals(first, buffer) && first.Width == 2 && first.Height == 3,
                "PiP allocates the requested capture buffer");
            check(ReferenceEquals(first, PipWindow.EnsureBuffer(ref buffer, 2, 3)),
                "PiP reuses its buffer for unchanged frame dimensions");

            var resized = PipWindow.EnsureBuffer(ref buffer, 2, 4);
            check(!ReferenceEquals(first, resized) && resized.Width == 2 && resized.Height == 4 && IsDisposed(first),
                "PiP replaces and disposes its buffer when only height changes");

            // Invalid dimensions force Bitmap's constructor to throw without
            // exhausting memory. This exercises the same allocation-failure path.
            bool failed = false;
            try { PipWindow.EnsureBuffer(ref buffer, 0, 4); }
            catch (ArgumentException) { failed = true; }
            check(failed && buffer == null && IsDisposed(resized),
                "PiP allocation failure releases and clears the old buffer");

            var recovered = PipWindow.EnsureBuffer(ref buffer, 2, 4);
            recovered.SetPixel(0, 0, Color.Red);
            check(recovered.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(),
                "PiP can capture again after a failed buffer allocation");
            check(ReferenceEquals(recovered, PipWindow.EnsureBuffer(ref buffer, 2, 4)),
                "PiP resumes buffer reuse after allocation recovery");

            var wider = PipWindow.EnsureBuffer(ref buffer, 3, 4);
            check(wider.Width == 3 && wider.Height == 4 && IsDisposed(recovered),
                "PiP replaces and disposes its buffer when only width changes");
        }
        finally { buffer?.Dispose(); }
    }

    private static bool IsDisposed(Bitmap bitmap)
    {
        try { _ = bitmap.Width; return false; }
        catch (ArgumentException) { return true; }
    }
}
