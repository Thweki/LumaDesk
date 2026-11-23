// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors
using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    private void AddApplicationOptions()
    {
        var ids = new[] { "auto", "light", "dark" };
        var theme = new ComboBox { ItemsSource = new[] { L.T("theme.auto"), L.T("theme.light"), L.T("theme.dark") }, SelectedIndex = Math.Max(0, Array.IndexOf(ids, Preferences.Theme)) };
        theme.SelectionChanged += (_, _) => { Preferences.Theme = ids[theme.SelectedIndex]; Save(); ThemeService.Apply(Preferences.Theme); Render(); quick?.Render(); };
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Text(L.T("theme.title")), theme)));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(L.T("automation.restore"), L.T("automation.restoreHelp")), Toggle(Preferences.RestoreBrightness, value => { Preferences.RestoreBrightness = value; Save(); }))));
        var idleDelay = new ComboBox
        {
            ItemsSource = Enumerable.Range(1, 60).Select(minutes => L.F("idle.minutes", minutes)).ToArray(),
            SelectedIndex = Preferences.IdleDimmingMinutes is >= 1 and <= 60 ? Preferences.IdleDimmingMinutes - 1 : 4,
            IsEnabled = Preferences.ExperimentalIdleDimming
        };
        idleDelay.SelectionChanged += (_, _) =>
        {
            Preferences.IdleDimmingMinutes = idleDelay.SelectedIndex + 1;
            ConfigureIdleDimming(); Save();
        };
        var idleToggle = Toggle(Preferences.ExperimentalIdleDimming, value =>
        {
            Preferences.ExperimentalIdleDimming = value; idleDelay.IsEnabled = value;
            ConfigureIdleDimming(); Save();
        });
        AutomationProperties.SetAutomationId(idleToggle, "experimental-idle-dimming");
        AutomationProperties.SetName(idleToggle, L.T("idle.title"));
        AutomationProperties.SetAutomationId(idleDelay, "idle-dimming-delay");
        AutomationProperties.SetName(idleDelay, L.T("idle.delay"));
        var idleLabels = Ui.Labels(L.T("idle.title"), L.T("idle.help"));
        idleLabels.Children.OfType<TextBlock>().Last().TextWrapping = TextWrapping.Wrap;
        PageContent.Children.Add(Ui.Card(Ui.Row(idleLabels, idleToggle)));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Text(L.T("idle.delay")), idleDelay)));
    }
}
