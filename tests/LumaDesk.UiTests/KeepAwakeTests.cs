// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LumaDesk;

internal static partial class Program
{
    private static void TestKeepAwake(MainWindow owner)
    {
        string? original = Environment.GetEnvironmentVariable(PanelKeepAwake.Flag);
        QuickWindow? quick = null;
        int acquired = 0, released = 0;
        bool fail = false;
        IDisposable Acquire()
        {
            acquired++;
            if (fail) throw new Win32Exception(5);
            return new WakeLease(() => released++);
        }
        CheckBox Toggle() => Descendants<CheckBox>(quick!).Single(c => AutomationProperties.GetAutomationId(c) == "quick-keep-awake");
        void Set(bool value)
        {
            var toggle = Toggle(); toggle.IsChecked = value;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        try
        {
            Environment.SetEnvironmentVariable(PanelKeepAwake.Flag, null);
            quick = new QuickWindow(owner, dismissOnDeactivate: false, acquireWakeRequest: Acquire);
            quick.Render();
            Check(!Descendants<CheckBox>(quick).Any(c => AutomationProperties.GetAutomationId(c) == "quick-keep-awake") && acquired == 0,
                "Keep-awake control is absent by default");
            quick.Close();

            Environment.SetEnvironmentVariable(PanelKeepAwake.Flag, "1");
            quick = new QuickWindow(owner, dismissOnDeactivate: false, acquireWakeRequest: Acquire);
            quick.Render(); quick.Show(); quick.UpdateLayout();
            Check(Toggle().IsChecked == false && acquired == 0, "Opt-in exposes an unchecked toggle");
            Set(true); quick.Render();
            Check(Toggle().IsChecked == true && acquired == 1 && released == 0,
                "Enabling and rerendering retain one wake request");
            Set(false);
            Check(Toggle().IsChecked == false && released == 1, "Unchecking releases the request immediately");
            fail = true; Set(true);
            Check(Toggle().IsChecked == false && Descendants<TextBlock>(quick).Any(t => AutomationProperties.GetAutomationId(t) == "quick-keep-awake-error"),
                "Native failure leaves the toggle off and displays an error");
            fail = false; Set(true);
            Check(Toggle().IsChecked == true && !Descendants<TextBlock>(quick).Any(t => AutomationProperties.GetAutomationId(t) == "quick-keep-awake-error"),
                "Retry succeeds and clears the error");
            quick.Hide();
            Check(released == 2, "Hiding releases the live request");
            quick.Show(); quick.UpdateLayout();
            Check(Toggle().IsChecked == false && acquired == 3, "Showing again starts off without reacquiring");
            Set(true);
            var hold = quick.KeepOpen();
            quick.Close();
            Check(quick.IsVisible && released == 2, "Cancelled close keeps the visible panel's request");
            hold.Dispose(); quick.Close();
            Check(released == 3, "Completed close releases the request");
            quick = new QuickWindow(owner, dismissOnDeactivate: false, acquireWakeRequest: Acquire);
            quick.Render();
            Check(Toggle().IsChecked == false && acquired == 4, "Replacement tray panel starts off");
        }
        finally
        {
            quick?.Close();
            Environment.SetEnvironmentVariable(PanelKeepAwake.Flag, original);
        }
    }

    private sealed class WakeLease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
