using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LumaDesk;

public partial class MainWindow : Window
{
    internal readonly DisplayService Service = new();
    internal readonly Preferences Preferences = Storage.Load();
    internal List<DisplayInfo> Displays = new();
    private readonly Dictionary<string, DimWindow> dimWindows = new();
    private readonly Dictionary<string, Button> navButtons = new();
    private readonly List<PipWindow> pipWindows = new();
    private readonly DispatcherTimer hotplugTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Forms.NotifyIcon? tray;
    private QuickWindow? quick;
    private string page = "显示器";
    private string? selectedId;
    private bool scanning, exiting, modePending;
    private IntPtr hwnd;
    internal DisplayInfo? Selected => Displays.FirstOrDefault(d => d.Id == selectedId) ?? Displays.FirstOrDefault();

    public MainWindow()
    {
        InitializeComponent();
        HelpSwitch.IsChecked = Preferences.ShowHelp;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(FitToMonitor);
        AddNavigation("显示器", "\uE7F4"); AddNavigation("显示器组", "\uE8A9"); AddNavigation("应用", "\uE713"); AddNavigation("快捷面板", "\uE8A5"); AddNavigation("键盘", "\uE765"); AddNavigation("预设", "\uE734"); AddNavigation("关于", "\uE946");
        hotplugTimer.Tick += (_, _) => { hotplugTimer.Stop(); if (!modePending) _ = RefreshAsync(); };
        Loaded += async (_, _) => { FitToMonitor(); SetupTray(); await RefreshAsync(); };
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowProc);
            int rounded = 2; Native.DwmSetWindowAttribute(hwnd, 33, ref rounded, 4);
            ConfigureHotkeys();
        };
        Closing += (_, e) =>
        {
            if (!exiting && Preferences.CloseToTray) { e.Cancel = true; Hide(); return; }
            if (modePending) { e.Cancel = true; return; }
            ShutdownResources();
        };
    }
    private void AddNavigation(string name, string icon)
    {
        var button = Ui.Button("", () => { page = name; selectedId = null; Render(); });
        button.Style = (Style)FindResource("Nav"); button.Content = Ui.IconLabel(icon, name); navButtons[name] = button; Navigation.Children.Add(button);
    }
    private void FitToMonitor()
    {
        if (hwnd == IntPtr.Zero || WindowState != WindowState.Normal) return;
        var work = Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double availableWidth = Math.Max(320, work.Width / dpi.DpiScaleX - 16), availableHeight = Math.Max(280, work.Height / dpi.DpiScaleY - 16);
        MinWidth = Math.Min(860, availableWidth); MinHeight = Math.Min(650, availableHeight);
        Width = Math.Min(Width, availableWidth); Height = Math.Min(Height, availableHeight);
    }
    internal void Status(string message, bool error = false) { StatusLabel.Text = message; StatusLabel.Foreground = Ui.Brush(error ? "#B45136" : "#777F8E"); Storage.Log(message); }
    internal async Task RunAsync(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) { Status(ex.Message, true); MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    internal async Task RefreshAsync()
    {
        if (scanning || modePending) return;
        scanning = true; Status("正在读取显示器信息与硬件控制能力…");
        try
        {
            var next = await Service.ScanAsync();
            foreach (var display in next) display.Dimming = Displays.FirstOrDefault(d => d.Id == display.Id)?.Dimming ?? 0;
            Displays = next;
            foreach (var id in dimWindows.Keys.ToArray())
            {
                var display = Displays.FirstOrDefault(d => d.Id == id);
                if (display == null) { dimWindows[id].Close(); dimWindows.Remove(id); }
                else dimWindows[id].Position(display);
            }
            Render(); quick?.Render(); Status($"●  {Displays.Count} 台显示器已连接   ·   上次检测 {DateTime.Now:HH:mm}");
        }
        catch (Exception ex) { Status("检测失败：" + ex.Message, true); Render(); }
        finally { scanning = false; }
    }
    private void Render()
    {
        foreach (var pair in navButtons) pair.Value.Background = Ui.Brush(pair.Key == page ? "#E5E5E5" : "#00FFFFFF");
        Breadcrumb.Text = "设置 / " + page;
        PageContent.Children.Clear(); DisplayTabs.Children.Clear();
        BuildTabs();
        switch (page)
        {
            case "显示器": if (selectedId != null && Selected != null) RenderDisplay(Selected); else RenderOverview(); break;
            case "显示器组": RenderGroups(); break;
            case "应用": RenderApplication(); break;
            case "快捷面板": RenderMenu(); break;
            case "键盘": RenderKeyboard(); break;
            case "预设": RenderPresets(); break;
            case "关于": RenderAbout(); break;
        }
    }
    internal void RefreshView() { if (IsVisible) Render(); }
    private void BuildTabs()
    {
        AddTab("概览", "\uE8FD", "所有显示器", selectedId == null, () => { page = "显示器"; selectedId = null; Render(); });
        foreach (var display in Displays)
        {
            var d = display; AddTab(d.Name, "\uE7F4", d.Primary ? "主显示器" : "外接显示器", selectedId == d.Id, () => { page = "显示器"; selectedId = d.Id; Render(); });
        }
    }
    private void AddTab(string title, string icon, string subtitle, bool active, Action action)
    {
        var stack = new StackPanel { Width = 130 };
        var name = Ui.Text(title, 12, active ? "#27344A" : "#606977", active); name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap; name.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(name);
        var sub = Ui.Text(subtitle, 10, "#6C737A"); sub.HorizontalAlignment = HorizontalAlignment.Center; sub.Margin = new Thickness(0, 3, 0, 0); stack.Children.Add(sub);
        var button = Ui.Button("", action); button.Content = stack; button.ToolTip = title; button.Padding = new Thickness(9, 7, 9, 7); button.Margin = new Thickness(0, 0, 8, 0); button.Background = Ui.Brush(active ? "#E6F2F4" : "#FFFFFF"); button.BorderBrush = Ui.Brush(active ? "#007A8A" : "#E0E0E0"); DisplayTabs.Children.Add(button);
    }
    private void Heading(string title, string? description = null)
    {
        var heading = Ui.Text(title, 22, "#242A35", true); heading.Margin = new Thickness(0, 0, 0, 7); PageContent.Children.Add(heading);
        if (description != null) { var sub = Ui.Text(description, 12, "#858C99"); sub.Margin = new Thickness(0, 0, 0, 22); PageContent.Children.Add(sub); }
    }
    private string? Help(string text) => Preferences.ShowHelp ? text : null;
    private void RenderOverview()
    {
        Heading("显示器概览", "调整显示配置、管理屏幕排列与常用控制。");
        var head = Ui.Row(Ui.Text("已连接的显示器", 13, "#626B7A", true), Ui.Button("↻  重新检测", () => _ = RefreshAsync())); head.Margin = new Thickness(0, 0, 0, 12); PageContent.Children.Add(head);
        if (Displays.Count == 0) PageContent.Children.Add(Ui.Card(Ui.Labels("尚未检测到活动显示器", "请连接显示器后重新检测。远程桌面可能不提供物理显示器控制接口。")));
        else
        {
            var list = new StackPanel();
            foreach (var d in Displays)
            {
                if (list.Children.Count > 0) list.Children.Add(Ui.Line());
                var left = new Grid(); left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); left.ColumnDefinitions.Add(new ColumnDefinition());
                var icon = new Border { Child = Ui.Icon("\uE7F4", 25, "#FFFFFF"), Background = Ui.Brush("#007A8A"), CornerRadius = new CornerRadius(10), Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Left }; left.Children.Add(icon);
                var labels = Ui.Labels(d.Name, d.Subtitle); Grid.SetColumn(labels, 1); left.Children.Add(labels);
                var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(Ui.Badge(d.Primary ? "主显示器" : "已连接", d.Primary ? "#007A8A" : "#38886F", d.Primary ? "#E6F2F4" : "#E6F2EC"));
                var open = Ui.Button("›", () => { selectedId = d.Id; Render(); }); open.FontSize = 22; open.Background = Brushes.Transparent; open.BorderThickness = new Thickness(0); open.Margin = new Thickness(10, 0, 0, 0); open.ToolTip = "打开 " + d.Name; actions.Children.Add(open);
                list.Children.Add(Ui.Row(left, actions));
            }
            PageContent.Children.Add(Ui.Card(list));
            var layout = new StackPanel(); layout.Children.Add(Ui.Row(Ui.Text("显示器排列", 13, "#515B6A", true), Ui.Text("位置来自 Windows", 10, "#949BA8")));
            var canvas = new Canvas { Height = 132, Margin = new Thickness(0, 12, 0, 2), ClipToBounds = true };
            canvas.SizeChanged += (_, _) => DrawArrangement(canvas); layout.Children.Add(canvas);
            layout.Children.Add(Ui.Row(Ui.Text("拖动排列、设置主屏或复制桌面", 11, "#8C94A0"), Ui.Button("编辑排列…", ShowLayout))); PageContent.Children.Add(Ui.Card(layout));
        }
        var virtualContent = new StackPanel(); virtualContent.Children.Add(Ui.Row(Ui.IconLabel("\uE7F8", "虚拟屏幕"), Ui.Button("管理虚拟屏幕…", ShowVirtualDisplays)));
        var desc = Ui.Text("在应用内安装签名驱动、添加和移除虚拟屏幕。创建后可直接调整分辨率、缩放和位置。", 12, "#858C99"); desc.Margin = new Thickness(0, 12, 0, 0); virtualContent.Children.Add(desc); PageContent.Children.Add(Ui.Card(virtualContent));
        var bottom = Ui.Row(Ui.Labels("快速访问", Help("通过系统托盘，随时调节亮度与显示模式。")), Ui.Button("打开快捷面板", ShowQuick)); PageContent.Children.Add(Ui.Card(bottom));
    }
    private void DrawArrangement(Canvas canvas)
    {
        canvas.Children.Clear(); if (Displays.Count == 0 || canvas.ActualWidth <= 0) return;
        double minX = Displays.Min(d => d.X), minY = Displays.Min(d => d.Y);
        double totalW = Displays.Max(d => d.X + d.Current.Width) - minX, totalH = Displays.Max(d => d.Y + d.Current.Height) - minY;
        double scale = Math.Min((canvas.ActualWidth - 42) / totalW, 108 / totalH);
        foreach (var d in Displays)
        {
            bool compact = d.Current.Height * scale < 50;
            var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Ui.Text(d.Number.ToString(), compact ? 18 : 24, d.Primary ? "#3F74D2" : "#717F98", true); number.HorizontalAlignment = HorizontalAlignment.Center; inner.Children.Add(number);
            var label = Ui.Text(d.Name, 10, "#7C8DA7"); label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextWrapping = TextWrapping.NoWrap; label.HorizontalAlignment = HorizontalAlignment.Center; if (!compact) inner.Children.Add(label);
            var screen = Ui.Button("", () => { selectedId = d.Id; Render(); }); screen.Content = inner; screen.Width = Math.Max(32, d.Current.Width * scale - 4); screen.Height = Math.Max(26, d.Current.Height * scale - 4); screen.Padding = new Thickness(4); screen.Background = new LinearGradientBrush(Ui.Brush("#D6E5FB").Color, Ui.Brush("#EDF0FA").Color, 65); screen.BorderBrush = Ui.Brush(d.Primary ? "#84ABEC" : "#BBC9DF"); screen.ToolTip = $"{d.Name}\n{d.Subtitle}\n坐标 {d.X}, {d.Y}";
            Canvas.SetLeft(screen, (canvas.ActualWidth - totalW * scale) / 2 + (d.X - minX) * scale); Canvas.SetTop(screen, 10 + (d.Y - minY) * scale); canvas.Children.Add(screen);
        }
    }
    private void RenderDisplay(DisplayInfo display)
    {
        Heading(display.Name, display.Subtitle + (display.Primary ? "   ·   主显示器" : ""));
        RenderDpiScaling(display);
        var light = new StackPanel(); light.Children.Add(Ui.Row(Ui.IconLabel("\uE706", "亮度与图像"), Ui.Badge(display.HasDdc ? "DDC/CI 已连接" : display.HasWmi ? "内置背光" : "软件调光可用")));
        light.Children.Add(Ui.Line());
        light.Children.Add(CreateSlider("组合亮度", GetCombinedBrightness(display), 100, value => ChangeCombinedBrightness(display, value), Help(display.Brightness.HasValue ? "20–100% 调整背光；0–20% 衔接软件调暗。0% 为最低亮度，不关闭屏幕。" : "此屏幕没有背光接口，使用软件调暗。")));
        light.Children.Add(Ui.Line());
        light.Children.Add(CreateSlider("硬件亮度", display.Brightness, 100, value => ChangeBrightness(display, value), Help(display.BrightnessMethod)));
        light.Children.Add(Ui.Line());
        light.Children.Add(CreateSlider("额外调暗", display.Dimming, 85, value => { SetDimming(display, value); return Task.CompletedTask; }, Help("降低画面亮度，不改变硬件背光；退出应用后自动恢复。")));
        if (display.Contrast.HasValue) { light.Children.Add(Ui.Line()); light.Children.Add(CreateSlider("对比度", display.Contrast, 100, async value => { await Service.SetPercentAsync(display, 0x12, value); display.Contrast = value; Status("对比度已更新"); })); }
        if (display.Volume.HasValue) { light.Children.Add(Ui.Line()); light.Children.Add(CreateSlider("显示器音量", display.Volume, 100, async value => { await Service.SetPercentAsync(display, 0x62, value); display.Volume = value; Status("显示器音量已更新"); })); }
        PageContent.Children.Add(Ui.Card(light));
        var mode = new StackPanel(); mode.Children.Add(Ui.IconLabel("\uE7F4", "输出信号 · 高级设置")); mode.Children.Add(Ui.Line());
        var resolutions = display.Modes.Select(m => m.Resolution).Distinct().ToList();
        var resolution = new ComboBox { ItemsSource = resolutions, SelectedItem = display.Current.Resolution, Width = 185 };
        var refresh = new ComboBox { Width = 185 }; var orientation = new ComboBox { ItemsSource = new[] { "横向 · 0°", "纵向 · 90°", "横向翻转 · 180°", "纵向翻转 · 270°" }, SelectedIndex = (int)display.Orientation, Width = 185 };
        void UpdateRates() { var rates = display.Modes.Where(m => m.Resolution == (string?)resolution.SelectedItem).Select(m => m.Frequency).Distinct().ToList(); refresh.ItemsSource = rates; refresh.SelectedItem = rates.Contains(display.Current.Frequency) ? display.Current.Frequency : rates.FirstOrDefault(); }
        UpdateRates(); resolution.SelectionChanged += (_, _) => UpdateRates();
        mode.Children.Add(Ui.Row(Ui.Labels("输出分辨率", Help("会改变像素输出；放大界面请使用上方的 HiDPI 缩放。")), resolution)); mode.Children.Add(Ui.Line()); mode.Children.Add(Ui.Row(Ui.Labels("刷新率", "Hz"), refresh)); mode.Children.Add(Ui.Line()); mode.Children.Add(Ui.Row(Ui.Text("屏幕方向"), orientation)); mode.Children.Add(Ui.Line());
        mode.Children.Add(Ui.Row(Ui.Text("驱动验证后立即应用；读取不匹配时恢复原模式。", 11, "#8B929E"), Ui.Button("应用显示模式", () => _ = RunAsync(async () =>
        {
            var selected = display.Modes.First(m => m.Resolution == (string?)resolution.SelectedItem && m.Frequency == (uint)refresh.SelectedItem);
            await ChangeModeAsync(display, selected, (uint)orientation.SelectedIndex);
        }), true))); PageContent.Children.Add(Ui.Card(mode));
        var extras = new StackPanel(); extras.Children.Add(Ui.Row(Ui.Labels("画中画", Help("将此显示器的实时画面放在一个置顶窗口中。")), Ui.Button("打开画中画", () => OpenPip(display)))); extras.Children.Add(Ui.Line());
        extras.Children.Add(CreateColorControls(display)); extras.Children.Add(Ui.Line());
        extras.Children.Add(Ui.Row(Ui.Labels("显示器 EDID", Help("导出原始显示器标识数据，用于诊断。")), Ui.Button("导出 EDID…", () => ExportEdid(display)))); PageContent.Children.Add(Ui.Card(extras));
        PageContent.Children.Add(Ui.Card(Ui.Labels("设备信息", $"{display.Adapter}\n{display.Device}  ·  桌面坐标 {display.X}, {display.Y}\n{display.Id}")));
    }
    internal UIElement CreateSlider(string label, int? value, int maximum, Func<int, Task> changed, string? help = null, bool compact = false)
    {
        var stack = new StackPanel(); var amount = Ui.Text(value.HasValue ? value + "%" : "不支持", 12, "#7C8594"); stack.Children.Add(Ui.Row(Ui.Labels(label, help), amount));
        var slider = new Slider { Value = value ?? 0, Maximum = maximum, IsEnabled = value.HasValue, Margin = new Thickness(0, compact ? 1 : 8, 0, 0), Height = compact ? 22 : 28 }; System.Windows.Automation.AutomationProperties.SetName(slider, label);
        int confirmed = value ?? 0; bool updating = false, failed = false; IDisposable? keepOpen = null;
        var writer = new LatestValueWriter(changed, (requested, error) =>
        {
            failed = error != null;
            if (error == null) { confirmed = requested; amount.ToolTip = null; return; }
            Status(error.Message, true);
            // An older failed request must not overwrite a newer gesture.
            if ((int)Math.Round(slider.Value) == requested)
            {
                updating = true; slider.Value = confirmed; updating = false;
                amount.Text = "失败 · " + confirmed + "%"; amount.ToolTip = error.Message;
            }
        }, idle: () => { keepOpen?.Dispose(); keepOpen = null; if (!failed && slider.IsLoaded && !slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) quick?.Render(); });
        slider.ValueChanged += (_, _) =>
        {
            amount.Text = Math.Round(slider.Value) + "%";
            if (!updating) { keepOpen ??= (Window.GetWindow(slider) as QuickWindow)?.KeepOpen(); writer.Request((int)Math.Round(slider.Value)); }
        };
        slider.Unloaded += (_, _) => { writer.Stop(); keepOpen?.Dispose(); keepOpen = null; };
        slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler(async (_, _) =>
        {
            await writer.WhenIdle;
            if (!failed && slider.IsLoaded) quick?.Render();
        }));
        stack.Children.Add(slider); return stack;
    }
    internal async Task ChangeBrightness(DisplayInfo display, int value)
    {
        var targets = Preferences.SyncBrightness ? Displays.Where(d => d.Brightness.HasValue).ToList() : new List<DisplayInfo> { display };
        var failed = new List<string>();
        foreach (var d in targets)
        {
            try { await Service.SetPercentAsync(d, 0x10, value); d.Brightness = value; }
            catch (Exception ex) { failed.Add(d.Name); Storage.Log(ex.Message); }
        }
        if (failed.Count > 0) throw new InvalidOperationException("以下显示器未能更新亮度：" + string.Join("、", failed) + "。其他已成功的屏幕不会自动撤销。请重新检测读取实际值。");
        Status(Preferences.SyncBrightness ? $"已同步 {targets.Count} 台显示器亮度至 {value}%" : $"{display.Name} · 亮度 {value}%");
    }
    internal void SetDimming(DisplayInfo display, int value)
    {
        value = Math.Clamp(value, 0, 85);
        if (value > 0) DisplayService.ValidateIdentity(display.Device, display.Id);
        if (value == 0) { if (dimWindows.Remove(display.Id, out var existing)) existing.Close(); }
        else { if (!dimWindows.TryGetValue(display.Id, out var overlay)) { overlay = new DimWindow(); dimWindows[display.Id] = overlay; overlay.Show(); } overlay.Position(display); overlay.Opacity = value / 100.0; }
        display.Dimming = value; Status($"{display.Name} · 软件调暗 {value}%");
    }
    internal async Task ChangeModeAsync(DisplayInfo display, DisplayMode mode, uint orientation, Window? dialogOwner = null)
    {
        if (modePending) throw new InvalidOperationException("正在应用显示设置，请稍后重试。");
        if (mode == display.Current && orientation == display.Orientation) { Status("当前已使用此显示模式"); return; }
        var original = DisplayService.CurrentMode(display.Device); var target = DisplayService.FindMode(display, mode, orientation);
        DisplayService.ApplyMode(display.Device, target, true, display.Id);
        string recovery = ModeRecovery.Prepare(display.Device, display.Id, original); modePending = true; ModeLease? lease = null;
        try
        {
            lease = new ModeLease(() => DisplayService.ApplyMode(display.Device, target, expectedId: display.Id), () => DisplayService.ApplyMode(display.Device, original, expectedId: display.Id));
            // Menu selection commits after driver readback; no extra Apply/Keep dialog.
            // The recovery process remains armed until this check succeeds.
            bool matches = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                DisplayService.ValidateIdentity(display.Device, display.Id);
                var actual = DisplayService.CurrentMode(display.Device);
                matches = actual.Width == target.Width && actual.Height == target.Height && actual.Frequency == target.Frequency && actual.Orientation == target.Orientation;
                if (matches) break;
                await Task.Delay(50);
            }
            if (!matches) throw new InvalidOperationException("驱动未确认请求的显示模式，正在恢复原设置。");
            File.Delete(recovery); lease.Keep(); Status("显示模式已应用（本次 Windows 会话）");
        }
        finally
        {
            if (lease?.Pending == true) { try { lease.Revert(); File.Delete(recovery); } catch (Exception ex) { Storage.Log("Rollback: " + ex); } }
            modePending = false; await RefreshAsync();
        }
    }
    private void RenderGroups()
    {
        Heading("显示器组", "让多块屏幕保持相同的亮度百分比。");
        var content = new StackPanel(); content.Children.Add(Ui.Row(Ui.Labels("所有可控制的显示器", Help("开启后，组合亮度同步到所有屏幕；独立硬件亮度只同步到支持背光控制的屏幕。")), Toggle(Preferences.SyncBrightness, v => { Preferences.SyncBrightness = v; Save(); })));
        foreach (var d in Displays) { content.Children.Add(Ui.Line()); content.Children.Add(Ui.Row(Ui.Labels(d.Name, d.BrightnessMethod), Ui.Badge(d.Brightness.HasValue ? "背光与调暗" : "软件调暗", d.Brightness.HasValue ? "#007A8A" : "#9298A2"))); }
        PageContent.Children.Add(Ui.Card(content)); PageContent.Children.Add(Ui.Card(Ui.Labels("亮度同步说明", "不同显示器的相同百分比不代表相同物理亮度。当前版本按百分比同步，不进行尼特校准。")));
    }
    private CheckBox Toggle(bool value, Action<bool> changed) { var toggle = new CheckBox { IsChecked = value }; toggle.Click += (_, _) => changed(toggle.IsChecked == true); return toggle; }
    internal void SavePreferences() => Save();
    private void Save() { try { Storage.Save(Preferences); } catch (Exception ex) { Status("设置保存失败：" + ex.Message, true); } }
    private void RenderApplication()
    {
        Heading("应用", "按你的方式，让 LumaDesk 陪伴每一天。");
        var content = new StackPanel(); content.Children.Add(Ui.Row(Ui.Labels("关闭窗口后驻留托盘", Help("从系统托盘重新打开；通过托盘菜单可完全退出。")), Toggle(Preferences.CloseToTray, v => { Preferences.CloseToTray = v; Save(); })));
        content.Children.Add(Ui.Line()); content.Children.Add(Ui.Row(Ui.Labels("开机自启", Help("登录 Windows 后自动启动 LumaDesk 并驻留托盘，仅对当前用户生效。")), Toggle(IsStartupEnabled(), SetStartup)));
        content.Children.Add(Ui.Line()); content.Children.Add(Ui.Row(Ui.Labels("显示帮助说明", "在设置下方显示功能说明。"), Toggle(Preferences.ShowHelp, v => { Preferences.ShowHelp = v; HelpSwitch.IsChecked = v; Save(); Render(); }))); PageContent.Children.Add(Ui.Card(content));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels("诊断信息", Help("导出显示器名称、设备标识、支持的模式与控制能力。")), Ui.Button("导出诊断…", ExportDiagnostics))));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels("恢复软件调光", "立即移除所有屏幕上的额外调暗。"), Ui.Button("恢复所有屏幕", ResetDimming))));
    }
    private static bool IsStartupEnabled() { using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"); return key?.GetValue("LumaDesk") != null; }
    private void SetStartup(bool enabled)
    {
        try { using var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"); if (enabled) key.SetValue("LumaDesk", "\"" + Environment.ProcessPath + "\" --tray"); else key.DeleteValue("LumaDesk", false); Status(enabled ? "已开启登录启动" : "已关闭登录启动"); }
        catch (Exception ex) { Status(ex.Message, true); Render(); }
    }
    private void RenderMenu()
    {
        Heading("快捷面板", "熟悉的控制中心，随时从 Windows 托盘打开。");
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels("打开快捷面板", Help("直接调整每台屏幕的亮度、HiDPI 档位、刷新率和显示模式。")), Ui.Button("打开面板", ShowQuick, true))));
        PageContent.Children.Add(Ui.Card(Ui.Labels("托盘操作", "单击显示器图标：打开快捷面板\n双击：打开完整设置\n右键：设置、恢复调光、重新检测或退出")));
    }
    private void RenderKeyboard()
    {
        Heading("键盘", "不离开当前工作，就能调整屏幕。");
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels("启用全局快捷键", Help("亮度快捷键作用于当前选中的屏幕，或亮度同步组。")), Toggle(Preferences.Hotkeys, v => { Preferences.Hotkeys = v; Save(); ConfigureHotkeys(); }))));
        var shortcuts = new StackPanel(); shortcuts.Children.Add(Ui.Row(Ui.Text("提高组合亮度 5%"), Ui.Badge("Ctrl + Alt + ↑"))); shortcuts.Children.Add(Ui.Line()); shortcuts.Children.Add(Ui.Row(Ui.Text("降低组合亮度 5%"), Ui.Badge("Ctrl + Alt + ↓"))); shortcuts.Children.Add(Ui.Line()); shortcuts.Children.Add(Ui.Row(Ui.Text("恢复所有软件调暗"), Ui.Badge("Ctrl + Alt + 0"))); PageContent.Children.Add(Ui.Card(shortcuts));
    }
    private void RenderPresets()
    {
        Heading("显示预设", "保存当前亮度与调暗设置，一键切换工作氛围。");
        var name = new TextBox { Text = "我的预设", MaxLength = 40, MinWidth = 200, Margin = new Thickness(0, 0, 12, 0) };
        PageContent.Children.Add(Ui.Card(Ui.Row(name, Ui.Button("保存当前设置", () =>
        {
            string label = name.Text.Trim(); if (label.Length == 0) { Status("请输入预设名称", true); return; }
            if (Displays.Count == 0) { Status("请先连接显示器", true); return; }
            if (Preferences.Presets.Any(p => p.Name.Equals(label, StringComparison.OrdinalIgnoreCase))) { Status("此预设名称已存在，请换一个名称", true); return; }
            Preferences.Presets.Add(new Preset { Name = label, Displays = Displays.Select(d => new PresetDisplay { Id = d.Id, Brightness = d.Brightness, Dimming = d.Dimming }).ToList() }); Save(); Render(); Status("预设已保存");
        }, true))));
        if (Preferences.Presets.Count == 0) PageContent.Children.Add(Ui.Card(Ui.Labels("还没有预设", "把当前设置保存为「工作」或「夜间」，以后随时恢复。")));
        foreach (var preset in Preferences.Presets.ToList())
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(Ui.Button("应用", () => _ = RunAsync(() => ApplyPresetAsync(preset))));
            var remove = Ui.Button("删除", () => { Preferences.Presets.Remove(preset); Save(); Render(); }); remove.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(remove);
            PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(preset.Name, $"{preset.Displays.Count} 台显示器 · 亮度与软件调暗"), actions)));
        }
    }
    private async Task ApplyPresetAsync(Preset preset)
    {
        var failures = new List<string>(); int count = 0;
        foreach (var saved in preset.Displays)
        {
            var d = Displays.FirstOrDefault(d => d.Id == saved.Id); if (d == null) { failures.Add("离线设备"); continue; }
            try { if (saved.Brightness.HasValue && d.Brightness.HasValue) { await Service.SetPercentAsync(d, 0x10, saved.Brightness.Value); d.Brightness = saved.Brightness.Value; } SetDimming(d, saved.Dimming); count++; }
            catch (Exception ex) { failures.Add(d.Name); Storage.Log(ex.Message); }
        }
        quick?.Render(); Status($"已应用「{preset.Name}」至 {count} 台显示器" + (failures.Count > 0 ? "；跳过或失败：" + string.Join("、", failures) : ""), failures.Count > 0);
    }
    private void RenderAbout()
    {
        Heading("LumaDesk for Windows", "独立实现 · 中文桌面版 · 0.3.0");
        PageContent.Children.Add(Ui.Card(Ui.Labels("关于 LumaDesk", "开源 Windows 显示管理工具。采用独立的 Fluent 风格界面与原创图标，通过 Windows 原生接口控制显示器。")));
        PageContent.Children.Add(Ui.Card(Ui.Labels("当前实现", "组合亮度、DDC/CI、内置背光、HiDPI 档位、分辨率 / 刷新率 / 旋转、逐屏 HDR 与 ICC 管理、拖动排列与主屏设置、扩展 / 复制桌面、签名虚拟显示驱动安装及屏幕数量控制、画中画、同步与快捷键。")));
        PageContent.Children.Add(Ui.Card(Ui.Labels("平台差异与待实现功能", "HiDPI 使用 Windows 每屏 DPI 缩放，并非 macOS 统一 2× 渲染后降采样。HDR 与 ICC 控制按 Windows 版本和驱动能力启用。Apple XDR、EDID 覆盖、3D LUT、网络电视、硬件输入源、布局保护与尼特校准仍未实现。虚拟屏幕驱动首次安装需要 Windows 管理员授权。")));
        PageContent.Children.Add(Ui.Card(Ui.Labels("开源许可", "MIT License · LumaDesk contributors\n虚拟显示驱动由 VirtualDrivers 项目提供，采用其独立许可证与签名。")));
    }
    private void SetupTray()
    {
        tray = new Forms.NotifyIcon { Text = "LumaDesk · 显示器控制", Icon = TrayIcon.Create(), Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("快捷面板", null, (_, _) => Dispatcher.Invoke(ShowQuick)); menu.Items.Add("显示器设置", null, (_, _) => Dispatcher.Invoke(ShowSettings)); menu.Items.Add("重新检测", null, (_, _) => Dispatcher.Invoke(() => _ = RefreshAsync())); menu.Items.Add("恢复所有软件调暗", null, (_, _) => Dispatcher.Invoke(ResetDimming)); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("退出 LumaDesk", null, (_, _) => Dispatcher.Invoke(Quit)); tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowQuick); }; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
    }
    internal void ShowSettings() { quick?.Close(); Show(); WindowState = WindowState.Normal; Activate(); }
    internal void ShowDisplay(DisplayInfo display) { selectedId = display.Id; page = "显示器"; Render(); ShowSettings(); }
    internal void ShowQuick()
    {
        if (quick == null) { quick = new QuickWindow(this); quick.Closed += (_, _) => quick = null; }
        quick.Render(); quick.Show(); quick.PositionNearTray(); quick.Activate();
    }
    internal void OpenPip(DisplayInfo display) { var pip = new PipWindow(display); pipWindows.Add(pip); pip.Closed += (_, _) => pipWindows.Remove(pip); pip.Show(); }
    private void ResetDimming() { foreach (var d in Displays) SetDimming(d, 0); Render(); quick?.Render(); Status("所有软件调暗已恢复"); }
    private void ConfigureHotkeys()
    {
        if (hwnd == IntPtr.Zero) return;
        for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(hwnd, i);
        if (!Preferences.Hotkeys) return;
        bool up = Native.RegisterHotKey(hwnd, 1, 0x4003, 0x26), down = Native.RegisterHotKey(hwnd, 2, 0x4003, 0x28), reset = Native.RegisterHotKey(hwnd, 3, 0x4003, 0x30);
        if (!up || !down || !reset) Status("部分快捷键已被其他应用占用", true);
    }
    private IntPtr WindowProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x7e || msg == 0x219 || msg == 0x2e0 || msg == 0x1a) { hotplugTimer.Stop(); hotplugTimer.Start(); }
        if (msg == 0x312)
        {
            handled = true;
            if (w.ToInt32() == 3) ResetDimming();
            else if (Selected is { } d) _ = RunAsync(async () => { await ChangeCombinedBrightness(d, Math.Clamp(GetCombinedBrightness(d) + (w.ToInt32() == 1 ? 5 : -5), 0, 100)); Render(); quick?.Render(); });
            else Status("当前没有选中的显示器", true);
        }
        return IntPtr.Zero;
    }
    internal static void OpenSystem(string uri) { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开设置"); } }
    private void ExportEdid(DisplayInfo d)
    {
        var bytes = DisplayService.ReadEdid(d); if (bytes == null) { Status("此显示器未提供可读取的 EDID 数据", true); return; }
        var dialog = new SaveFileDialog { FileName = "display-edid.bin", Filter = "EDID 二进制文件|*.bin" }; if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, bytes); Status("EDID 已导出"); } catch (Exception ex) { Status(ex.Message, true); }
    }
    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog { FileName = "lumadesk-diagnostics.json", Filter = "JSON|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(Displays, Storage.Json)); Status("诊断信息已导出"); } catch (Exception ex) { Status(ex.Message, true); }
    }
    private void ShutdownResources() { hotplugTimer.Stop(); for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(hwnd, i); tray?.Dispose(); foreach (var window in dimWindows.Values) window.Close(); dimWindows.Clear(); foreach (var window in pipWindows.ToArray()) window.Close(); quick?.Close(); Save(); System.Windows.Application.Current.Shutdown(); }
    internal void Quit() { if (modePending) return; exiting = true; Close(); }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OpenQuick(object sender, RoutedEventArgs e) => ShowQuick();
    private void HelpChanged(object sender, RoutedEventArgs e) { Preferences.ShowHelp = HelpSwitch.IsChecked == true; Save(); Render(); }
}
