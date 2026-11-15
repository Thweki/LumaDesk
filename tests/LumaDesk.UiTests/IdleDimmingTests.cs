// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

internal static partial class Program
{
    private static void TestIdleDimmingOptions(MainWindow owner)
    {
        Click(owner.Navigation.Children.OfType<Button>().ElementAt(2));
        var toggle = Descendants<CheckBox>(owner.PageContent).Single(c => AutomationProperties.GetAutomationId(c) == "experimental-idle-dimming");
        var delay = Descendants<ComboBox>(owner.PageContent).Single(c => AutomationProperties.GetAutomationId(c) == "idle-dimming-delay");
        Check(toggle.IsChecked == false && !delay.IsEnabled && delay.SelectedIndex == 4,
            "Experimental idle dimming defaults off and disables its five-minute delay control");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Check(owner.Preferences.ExperimentalIdleDimming && delay.IsEnabled, "Idle dimming can be explicitly enabled in Application settings");
        delay.SelectedIndex = 0;
        var saved = Storage.Load();
        Check(saved.ExperimentalIdleDimming && saved.IdleDimmingMinutes == 1, "Idle dimming setting and chosen delay persist");
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Check(!Storage.Load().ExperimentalIdleDimming && !delay.IsEnabled, "Turning off idle dimming persists and disables its delay control");
        Check(owner.Preferences.RememberedBrightness.Count == 0, "Configuring idle dimming never stores a brightness level");
    }
}
