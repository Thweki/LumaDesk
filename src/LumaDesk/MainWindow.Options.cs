// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors
using System;
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
    }
}
