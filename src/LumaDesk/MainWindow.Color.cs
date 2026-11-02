using Microsoft.Win32;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal UIElement CreateColorControls(DisplayInfo display, bool compact = false)
    {
        var section = new StackPanel();
        System.Windows.Automation.AutomationProperties.SetAutomationId(section, "color-controls-" + display.Number);
        var content = new StackPanel();
        var feedback = Ui.Text("正在读取此屏幕的颜色设置…", 11, "#7A8699");
        bool busy = false;

        async Task Reload()
        {
            if (busy) return;
            busy = true; content.IsEnabled = false;
            try
            {
                var state = await Task.Run(() => AdvancedColorService.Read(display));
                RenderState(state); feedback.Text = "";
            }
            catch (Exception ex) { feedback.Text = ex.Message; }
            finally { busy = false; content.IsEnabled = true; }
        }
        async Task Change(Action action, string success)
        {
            if (busy) return;
            using var hold = (Window.GetWindow(section) as QuickWindow)?.KeepOpen();
            busy = true; content.IsEnabled = false; feedback.Text = "正在应用…";
            try { await Task.Run(action); Status(success); feedback.Text = success; }
            catch (Exception ex) { feedback.Text = ex.Message; Status(ex.Message, true); }
            finally
            {
                try { RenderState(await Task.Run(() => AdvancedColorService.Read(display))); }
                catch (Exception ex) { feedback.Text += "\n" + ex.Message; }
                busy = false; content.IsEnabled = true;
            }
        }
        void RenderState(DisplayColorState state)
        {
            content.Children.Clear();
            var hdrStatus = state.HdrKnown ? (state.HdrActive ? "HDR 正在显示" : state.HdrUserEnabled ? "HDR 已开启，当前未激活" : "HDR 已关闭") : state.ColorMode;
            content.Children.Add(Ui.Row(Ui.Text("HDR", 12, "#354155", true), Ui.Text(hdrStatus, 11, "#7A8699")));
            var toggle = new CheckBox { Content = "使用 HDR", IsChecked = state.HdrUserEnabled, Margin = new Thickness(0, 8, 0, 5), IsEnabled = state.HdrKnown && state.HdrSupported && (!state.PolicyLimited || state.HdrUserEnabled) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, "color-hdr-toggle-" + display.Number);
            toggle.Click += async (_, _) => { bool enabled = toggle.IsChecked == true; await Change(() => AdvancedColorService.SetHdr(display, enabled), enabled ? "HDR 开关已开启" : "HDR 开关已关闭"); };
            content.Children.Add(toggle);
            content.Children.Add(Ui.Text($"{state.ColorMode} · {state.Encoding} · {state.BitsPerChannel} 位/通道", 11, "#7A8699"));
            if (!string.IsNullOrEmpty(state.HdrUnavailableReason)) content.Children.Add(Ui.Text(state.HdrUnavailableReason, 11, "#8B7766"));
            if (state.SdrWhiteNits.HasValue) content.Children.Add(Ui.Text($"SDR 白色亮度：{state.SdrWhiteNits:0.#} 尼特（系统读数）", 11, "#7A8699"));
            content.Children.Add(Ui.Line());
            content.Children.Add(Ui.Text("颜色配置文件（ICC）", 12, "#354155", true));
            if (!state.ProfilesAvailable)
            {
                content.Children.Add(Ui.Text(state.ProfileUnavailableReason, 11, "#8B7766"));
                return;
            }
            content.Children.Add(Ui.Text(state.UseUserProfiles ? "当前使用此屏幕的个人配置" : "当前沿用系统配置", 11, "#7A8699"));
            var kind = new ComboBox { ItemsSource = state.HdrSupported ? new[] { "SDR 配置", "HDR 配置" } : new[] { "SDR 配置" }, SelectedIndex = state.HdrActive ? 1 : 0, Margin = new Thickness(0, 8, 0, 6), MinWidth = 130 };
            var profile = new ComboBox { ItemsSource = state.Profiles, Margin = new Thickness(0, 4, 0, 6), MinWidth = 160, MaxDropDownHeight = 220 };
            var current = Ui.Text("", 11, "#7A8699");
            void SelectKind()
            {
                string active = kind.SelectedIndex == 1 ? state.HdrProfile : state.SdrProfile;
                current.Text = "当前：" + (string.IsNullOrEmpty(active) ? "无指定配置（系统默认）" : active);
                profile.SelectedItem = state.Profiles.FirstOrDefault(p => string.Equals(p, active, StringComparison.OrdinalIgnoreCase));
            }
            kind.SelectionChanged += (_, _) => SelectKind(); SelectKind();
            System.Windows.Automation.AutomationProperties.SetAutomationId(profile, "color-profile-list-" + display.Number);
            content.Children.Add(kind); content.Children.Add(current); content.Children.Add(profile);
            var apply = Ui.Button("设为此屏幕默认", () =>
            {
                if (profile.SelectedItem is not string name) { feedback.Text = "请先选择颜色配置文件。"; return; }
                bool hdr = kind.SelectedIndex == 1;
                _ = Change(() => AdvancedColorService.SetDefaultProfile(display, name, hdr), "已更新此屏幕的" + (hdr ? " HDR" : " SDR") + " 默认配置");
            });
            var import = Ui.Button("导入 ICC…", () =>
            {
                using var hold = (Window.GetWindow(section) as QuickWindow)?.KeepOpen();
                var chooser = new OpenFileDialog { Title = "导入并设为此屏幕的默认颜色配置", Filter = "ICC 颜色配置 (*.icc;*.icm)|*.icc;*.icm", CheckFileExists = true, Multiselect = false };
                if (chooser.ShowDialog(Window.GetWindow(section)) != true) return;
                bool hdr = kind.SelectedIndex == 1;
                _ = Change(() => AdvancedColorService.InstallAndSetProfile(display, chooser.FileName, hdr,
                    message => Dispatcher.Invoke(() => feedback.Text = message)), "颜色配置已导入并应用到此屏幕");
            });
            if (compact) { content.Children.Add(apply); import.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(import); }
            else content.Children.Add(Ui.Row(apply, import));
            if (state.UseUserProfiles)
            {
                var restore = Ui.Button("恢复此屏幕的系统配置", () => _ = Change(() => AdvancedColorService.UseSystemProfiles(display), "此屏幕已恢复使用系统颜色配置"));
                restore.Margin = new Thickness(0, 7, 0, 0); content.Children.Add(restore);
            }
            if (!compact) content.Children.Add(Ui.Text("导入与默认设置仅针对当前屏幕。SDR 与 HDR 分别选择；恢复系统配置会同时恢复两种模式。", 11, "#7A8699"));
        }
        section.Children.Add(Ui.Row(Ui.Text(compact ? "显示状态" : "色彩与 HDR 管理", compact ? 11 : 13, "#354155", true), Ui.Button("刷新", () => _ = Reload())));
        section.Children.Add(content); feedback.Margin = new Thickness(0, 7, 0, 0); section.Children.Add(feedback);
        section.Loaded += async (_, _) => await Reload();
        return section;
    }
}
