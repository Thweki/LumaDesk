// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using LumaDesk;

internal static class PanelKeepAwakeTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (string? value in new[] { null, "", "0", "true", "yes", " 1", "1 " })
            check(!PanelKeepAwake.IsEnabled(value), "Keep-awake flag requires explicit 1: " + value);
        check(PanelKeepAwake.IsEnabled("1"), "Keep-awake flag accepts 1");
        check(Marshal.SizeOf<DisplayWakeRequest.ReasonContext>() == (IntPtr.Size == 8 ? 32 : 24) &&
            Marshal.OffsetOf<DisplayWakeRequest.ReasonContext>("Reason").ToInt32() == 8,
            "Power request context matches the Windows ABI");

        int acquired = 0, released = 0;
        bool fail = false;
        IDisposable Acquire()
        {
            acquired++;
            if (fail) throw new Win32Exception(5);
            return new Lease(() => released++);
        }
        using (var disabled = new PanelKeepAwake(false, Acquire))
        {
            disabled.SetEnabled(true, true);
            check(!disabled.Active && acquired == 0, "Disabled feature cannot request wakefulness");
        }
        var session = new PanelKeepAwake(true, Acquire);
        check(!session.Active && acquired == 0, "Available feature starts off");
        session.SetEnabled(true, false);
        check(!session.Active && acquired == 0, "Hidden panel cannot acquire a request");
        session.SetEnabled(true, true); session.SetEnabled(true, true);
        check(session.Active && acquired == 1, "Repeated enable holds exactly one request");
        session.SetEnabled(false, true); session.SetEnabled(false, true);
        check(!session.Active && released == 1, "Toggle off releases exactly once");
        session.SetEnabled(true, true); session.SetEnabled(true, false);
        check(!session.Active && acquired == 2 && released == 2, "Hiding an active panel releases its request");
        fail = true;
        bool rejected = false;
        try { session.SetEnabled(true, true); } catch (Win32Exception) { rejected = true; }
        check(rejected && !session.Active && released == 2, "Failed acquisition is never reported as active");
        fail = false; session.SetEnabled(true, true);
        check(session.Active && acquired == 4, "Failed acquisition can be retried");
        session.Dispose(); session.Dispose(); session.SetEnabled(true, true);
        check(!session.Active && released == 3 && acquired == 4, "Disposal releases once and prevents reacquisition");
        using var reopened = new PanelKeepAwake(true, Acquire);
        check(!reopened.Active && acquired == 4, "A new panel starts off after an active panel closes");
    }

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
