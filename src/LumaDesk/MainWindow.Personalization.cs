// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal DisplayPreference Options(DisplayInfo display)
    {
        if (!Preferences.DisplayOptions.TryGetValue(display.Id, out var value)) Preferences.DisplayOptions[display.Id] = value = new();
        return value;
    }
    private void ApplyAlias(DisplayInfo display)
    {
        if (Preferences.DisplayOptions.TryGetValue(display.Id, out var value) && !string.IsNullOrWhiteSpace(value.Alias)) display.Name = value.Alias;
    }
    internal IEnumerable<DisplayInfo> OrderedDisplays() => Displays.OrderBy(d => Options(d).Order).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);
    internal List<DisplayInfo> BrightnessTargets(DisplayInfo source)
    {
        if (Preferences.SyncBrightness) return Displays.ToList();
        var group = Preferences.Groups.FirstOrDefault(g => g.Displays.Contains(source.Id, StringComparer.OrdinalIgnoreCase));
        return group == null ? new() { source } : Displays.Where(d => group.Displays.Contains(d.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    }
    private void RenderDisplayGroups()
    {
        Heading(L.T("s563a1990f3"), L.T("group.description"));
        PageContent.Children.Add(Ui.Card(Ui.Setting(L.T("s48aa89296c"), L.T("group.allHelp"), Toggle(Preferences.SyncBrightness, value => { Preferences.SyncBrightness = value; Save(); Render(); }))));
        var name = new TextBox { Text = L.T("group.default"), MaxLength = 40 };
        var add = Ui.Button(L.T("group.add"), () =>
        {
            string label = name.Text.Trim();
            if (label.Length == 0 || Preferences.Groups.Any(g => g.Name.Equals(label, StringComparison.OrdinalIgnoreCase))) return;
            Preferences.Groups.Add(new() { Name = label }); Save(); Render();
        });
        void ValidateName() => add.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) &&
            !Preferences.Groups.Any(g => g.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        name.TextChanged += (_, _) => ValidateName();
        ValidateName();
        PageContent.Children.Add(Ui.Card(Ui.Row(name, add)));
        foreach (var group in Preferences.Groups.ToArray())
        {
            var content = new StackPanel { IsEnabled = !Preferences.SyncBrightness };
            content.Children.Add(Ui.Row(Ui.Text(group.Name, 14, bold: true), Ui.Button(L.T("s2f9daa8289"), () => { Preferences.Groups.Remove(group); Save(); Render(); })));
            foreach (var d in Displays)
            {
                content.Children.Add(Ui.Line());
                content.Children.Add(Ui.Row(Ui.Text(d.Name), Toggle(group.Displays.Contains(d.Id), value =>
                {
                    foreach (var other in Preferences.Groups) other.Displays.RemoveAll(id => id.Equals(d.Id, StringComparison.OrdinalIgnoreCase));
                    if (value) group.Displays.Add(d.Id); Save(); Render();
                })));
            }
            PageContent.Children.Add(Ui.Card(content));
        }
        var tuning = new StackPanel();
        foreach (var d in Displays)
        {
            var options = Options(d); if (tuning.Children.Count > 0) tuning.Children.Add(Ui.Line()); tuning.Children.Add(Ui.Text(d.Name, 13, bold: true));
            var fields = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            TextBox Field(string label, int value)
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; panel.Children.Add(Ui.Text(label, 11));
                var input = new TextBox { Text = value.ToString(), Width = 85, MaxLength = 4 }; panel.Children.Add(input); fields.Children.Add(panel); return input;
            }
            var offset = Field(L.T("group.offset"), options.Offset); var min = Field(L.T("group.min"), options.Minimum); var max = Field(L.T("group.max"), options.Maximum);
            var save = Ui.Button(L.T("group.save"), () =>
            {
                if (!int.TryParse(offset.Text, out var o) || !int.TryParse(min.Text, out var lo) || !int.TryParse(max.Text, out var hi) || o < -100 || o > 100 || lo < 0 || hi > 100 || lo > hi)
                { Status(L.T("group.invalid"), true); return; }
                options.Offset = o; options.Minimum = lo; options.Maximum = hi; Save(); Status(L.T("group.saved"));
            });
            fields.Children.Add(save); tuning.Children.Add(fields);
        }
        if (Displays.Count > 0) PageContent.Children.Add(Ui.Card(Ui.Hint(new Expander { Header = L.T("group.tuning"), Content = tuning }, L.T("group.tuningHelp"))));
    }

    private void RenderPanelPreferences()
    {
        Heading(L.T("s770544f702"), L.T("panel.description"));
        var launch = Ui.Button(L.T("saf91a997fb"), ShowQuick);
        launch.HorizontalAlignment = HorizontalAlignment.Left; launch.Margin = new Thickness(0, 0, 0, 10);
        PageContent.Children.Add(launch);
        var screens = OrderedDisplays().ToArray();
        foreach (var display in screens)
        {
            var options = Options(display); var stack = new StackPanel();
            var alias = new TextBox { Text = display.Name, MaxLength = 40 };
            var save = Ui.Button(L.T("group.save"), () => { options.Alias = alias.Text.Trim(); Save(); if (options.Alias.Length > 0) display.Name = options.Alias; else _ = RefreshAsync(); Render(); quick?.Render(); });
            stack.Children.Add(Ui.Row(alias, save));
            var extra = new StackPanel();
            extra.Children.Add(Ui.Row(Ui.Text(L.T("panel.scaling")), Toggle(options.ShowScaling, value => { options.ShowScaling = value; Save(); quick?.Render(); })));
            var order = new StackPanel { Orientation = Orientation.Horizontal };
            void Move(int delta)
            {
                int index = Array.IndexOf(screens, display), target = index + delta;
                if (target < 0 || target >= screens.Length) return;
                (screens[index], screens[target]) = (screens[target], screens[index]);
                for (int i = 0; i < screens.Length; i++) Options(screens[i]).Order = i;
                Save(); Render(); quick?.Render();
            }
            var up = Ui.Button(L.T("panel.up"), () => Move(-1)); up.IsEnabled = display != screens[0];
            var down = Ui.Button(L.T("panel.down"), () => Move(1)); down.IsEnabled = display != screens[^1];
            down.Margin = new Thickness(6, 0, 0, 0); order.Children.Add(up); order.Children.Add(down);
            extra.Children.Add(Ui.Line()); extra.Children.Add(order);
            stack.Children.Add(new Expander { Header = L.T("scene.more"), Content = extra, Margin = new Thickness(0, 10, 0, 0) });
            PageContent.Children.Add(Ui.Card(stack));
        }
    }
}

internal static class GroupRules
{
    internal static int Target(int requested, DisplayPreference source, DisplayPreference target) =>
        Math.Clamp(requested - Math.Clamp(source.Offset, -100, 100) + Math.Clamp(target.Offset, -100, 100),
            Math.Clamp(target.Minimum, 0, 100), Math.Clamp(Math.Max(target.Minimum, target.Maximum), 0, 100));
}
