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
        stack.Children.Add(Ui.Row(Ui.IconLabel("\uE8D2", "HiDPI · 界面缩放"), Ui.Badge(info.CanSet ? $"当前 {info.CurrentPercent}%" : "暂不可用")));
        stack.Children.Add(Ui.Line());
        if (!info.CanSet)
        {
            stack.Children.Add(Ui.Labels("此屏幕暂不支持独立缩放", info.UnavailableReason));
            PageContent.Children.Add(Ui.Card(stack)); return;
        }
        stack.Children.Add(Ui.Row(Ui.Text("更多空间", 11, "#8790A0"), Ui.Text("更大文字", 11, "#8790A0")));
        int selected = info.CurrentPercent;
        var choices = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        var buttons = new Dictionary<int, Button>();
        var preview = Ui.Text("", 12, "#566378"); preview.Margin = new Thickness(0, 5, 0, 12);
        var apply = Ui.Button("应用缩放", () => _ = RunAsync(() => ChangeDpiAsync(display, selected)), true);
        void Select(int percent)
        {
            selected = percent;
            foreach (var item in buttons)
            {
                item.Value.Background = Ui.Brush(item.Key == percent ? "#E8F0FF" : "#FFFFFF");
                item.Value.BorderBrush = Ui.Brush(item.Key == percent ? "#3179F6" : "#DDE3ED");
            }
            preview.Text = $"看起来像约 {DpiRules.LooksLike(display.Current, percent)} · {percent}%";
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
            button.ToolTip = value == info.RecommendedPercent ? "Windows 推荐比例" : $"保持输出 {display.Current.Resolution}，调整界面尺寸";
            System.Windows.Automation.AutomationProperties.SetName(button, $"界面缩放 {value}%");
            buttons[value] = button; choices.Children.Add(button);
        }
        stack.Children.Add(choices); stack.Children.Add(preview); Select(selected);
        stack.Children.Add(Ui.Row(Ui.Labels("输出保持 " + display.Current.Resolution + $" · {display.Current.Frequency} Hz", Help("支持 DPI 的应用重新绘制文字与界面；部分旧软件可能仍会模糊。")), apply));
        if (display.PreferredWidth > 0 && (display.Current.Width != display.PreferredWidth || display.Current.Height != display.PreferredHeight))
        {
            stack.Children.Add(Ui.Line());
            var preferred = display.Modes.Where(m => m.Width == display.PreferredWidth && m.Height == display.PreferredHeight).OrderByDescending(m => m.Frequency == display.Current.Frequency).ThenByDescending(m => m.Frequency).FirstOrDefault();
            var explanation = Ui.Labels("当前输出不是显示器首选分辨率", $"首选 {display.PreferredWidth} × {display.PreferredHeight}。先恢复首选输出，再调整缩放，更利于保持清晰。" );
            if (preferred != null) stack.Children.Add(Ui.Row(explanation, Ui.Button("恢复首选输出…", () => _ = RunAsync(() => ChangeModeAsync(display, preferred, display.Orientation)))));
            else stack.Children.Add(explanation);
        }
        PageContent.Children.Add(Ui.Card(stack));
    }

    internal async Task ChangeDpiAsync(DisplayInfo display, int percent, Window? dialogOwner = null)
    {
        if (modePending) throw new InvalidOperationException("正在应用显示设置，请稍后重试。");
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
                throw new InvalidOperationException("检测到输出模式同时发生变化，正在恢复缩放。请重新检测后操作。");
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
