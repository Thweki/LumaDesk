// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    private void RenderDpiScaling(DisplayInfo display)
    {
        var info = display.DpiScale;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Row(Ui.IconLabel("\uE8D2", L.T("s1dc850889f")), Ui.Badge(info.CanSet ? L.F("s1386e4cc28", info.CurrentPercent) : L.T("sf36cac9622"))));
        stack.Children.Add(Ui.Line());
        if (!info.CanSet)
        {
            stack.Children.Add(Ui.Labels(L.T("s9d962e68b5"), info.UnavailableReason));
            PageContent.Children.Add(Ui.Card(stack)); return;
        }
        stack.Children.Add(Ui.Row(Ui.Text(L.T("s645f1dc4c0"), 11, "#8790A0"), Ui.Text(L.T("s1b4cf95f58"), 11, "#8790A0")));
        int selected = info.CurrentPercent;
        var choices = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        var buttons = new Dictionary<int, Button>();
        var preview = Ui.Text("", 12, "#566378"); preview.Margin = new Thickness(0, 5, 0, 12);
        var apply = Ui.Button(L.T("s2617b70da6"), () => _ = RunAsync(() => ChangeDpiAsync(display, selected)), true);
        void Select(int percent)
        {
            selected = percent;
            foreach (var item in buttons)
            {
                item.Value.Background = Ui.Brush(item.Key == percent ? "#E8F0FF" : "#FFFFFF");
                item.Value.BorderBrush = Ui.Brush(item.Key == percent ? "#3179F6" : "#DDE3ED");
            }
            preview.Text = L.F("seb1a7976a2", DpiRules.LooksLike(display.Current, percent), percent);
            apply.IsEnabled = selected != info.CurrentPercent;
        }
        foreach (int percentage in info.SupportedPercents)
        {
            int value = percentage;
            var content = new StackPanel();
            var sample = Ui.Text("Aa", Math.Min(30, 14 + value * 0.055), "#466DA7", true); sample.Height = 37; sample.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(sample);
            var label = Ui.Text($"{value}%", 12, "#45546A", true); label.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(label);
            var size = Ui.Text(DpiRules.LooksLike(display.Current, value), 10, "#8692A4"); size.HorizontalAlignment = HorizontalAlignment.Center; size.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(size);
            var button = Ui.Button("", () => Select(value)); button.Content = content; button.Width = 108; button.Margin = new Thickness(0, 0, 8, 8); button.Padding = new Thickness(6, 8, 6, 8);
            button.ToolTip = value == info.RecommendedPercent ? L.T("sb3f9480f4b") : L.F("s5015239d4d", display.Current.Resolution);
            System.Windows.Automation.AutomationProperties.SetName(button, L.F("s018e0d7c32", value));
            buttons[value] = button; choices.Children.Add(button);
        }
        stack.Children.Add(choices); stack.Children.Add(preview); Select(selected);
        stack.Children.Add(Ui.Row(Ui.Labels(L.T("s56cca15c6e") + display.Current.Resolution + $" · {display.Current.Frequency} Hz", Help(L.T("s5f4e417692"))), apply));
        if (display.PreferredWidth > 0 && (display.Current.Width != display.PreferredWidth || display.Current.Height != display.PreferredHeight))
        {
            stack.Children.Add(Ui.Line());
            var preferred = display.Modes.Where(m => m.Width == display.PreferredWidth && m.Height == display.PreferredHeight).OrderByDescending(m => m.Frequency == display.Current.Frequency).ThenByDescending(m => m.Frequency).FirstOrDefault();
            var explanation = Ui.Labels(L.T("s4d3e83e606"), L.F("sa3d8bd2b1b", display.PreferredWidth, display.PreferredHeight) );
            if (preferred != null) stack.Children.Add(Ui.Row(explanation, Ui.Button(L.T("sa3c47404b4"), () => _ = RunAsync(() => ChangeModeAsync(display, preferred, display.Orientation)))));
            else stack.Children.Add(explanation);
        }
        PageContent.Children.Add(Ui.Card(stack));
    }

    internal async Task ChangeDpiAsync(DisplayInfo display, int percent, Window? dialogOwner = null)
    {
        if (modePending) throw new InvalidOperationException(L.T("s70da07b61e"));
        modePending = true; ModeLease? lease = null; string? recovery = null;
        try
        {
            var info = await Task.Run(() => DpiService.Read(display)); DpiRules.ToRelative(info, percent);
            if (info.CurrentPercent == percent) { display.DpiScale = info; return; }
            var original = DisplayService.CurrentMode(display.Device);
            recovery = await Task.Run(() => DpiRecovery.Prepare(display, info.CurrentPercent));
            lease = await Task.Run(() => new ModeLease(() => DpiService.Set(display, percent), () => DpiService.Set(display, info.CurrentPercent)));
            var actual = DisplayService.CurrentMode(display.Device);
            if (actual.Width != original.Width || actual.Height != original.Height || actual.Frequency != original.Frequency || actual.Orientation != original.Orientation)
                throw new InvalidOperationException(L.T("s546905110c"));
            // DPI changes are committed after successful readback, matching the quick-menu
            // interaction. Hardware mode switching has a separate safety confirmation.
            File.Delete(recovery); lease.Keep();
        }
        finally
        {
            if (lease?.Pending == true) { try { await Task.Run(lease.Revert); if (recovery != null) File.Delete(recovery); } catch (Exception ex) { Storage.Log("DPI rollback: " + ex); } }
            try { display.DpiScale = await Task.Run(() => DpiService.Read(display)); }
            catch (Exception ex) { Storage.Log("DPI refresh: " + ex.Message); }
            modePending = false;
            // Avoid DDC enumeration and rebuilding the active quick slider per step.
            if (IsVisible) Render();
        }
    }
}
