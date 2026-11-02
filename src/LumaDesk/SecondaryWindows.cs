using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace LumaDesk;

internal sealed class QuickWindow : Window
{
    private readonly MainWindow owner;
    private readonly Func<DisplayInfo, int, Window, Task> applyDpi;
    private readonly Func<DisplayInfo, DisplayMode, uint, Window, Task> applyMode;
    private readonly Dictionary<string, string> modeSections = new();
    private readonly HashSet<string> colorSections = new();
    private bool dpiBusy;
    private string? activeDpiDisplay;
    private Slider? activeDpiSlider;
    private bool dpiDragging;
    private readonly bool dismissOnDeactivate;
    private int keepOpenCount;
    private bool dismissRequested, forceDismissRequested, closed;
    private readonly PopupPlacement placement;
    private double preferredHeight = 620;
    internal bool IsBackdropActive { get; private set; }
    private readonly Dictionary<string, string> dpiErrors = new();
    private readonly Dictionary<string, TextBlock> errorViews = new();
    private readonly StackPanel body = new() { Margin = new Thickness(6) };
    public QuickWindow(MainWindow owner, Func<DisplayInfo, int, Window, Task>? applyDpi = null, Func<DisplayInfo, DisplayMode, uint, Window, Task>? applyMode = null, bool dismissOnDeactivate = true)
    {
        this.owner = owner; this.applyDpi = applyDpi ?? ((display, percent, parent) => owner.ChangeDpiAsync(display, percent, parent));
        this.applyMode = applyMode ?? ((display, mode, orientation, parent) => owner.ChangeModeAsync(display, mode, orientation, parent));
        this.dismissOnDeactivate = dismissOnDeactivate;
        Title = AppBrand.Name + " · 快捷面板"; Width = 360; Height = 620; MinHeight = 180;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = false; ShowInTaskbar = false; Topmost = true;
        var surface = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#24000000"), Background = Ui.Brush("#F3F3F3"), Child = new ScrollViewer { Content = body } };
        surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), 12, 12);
        Content = surface; WindowEffects.ConfigurePopup(this, surface, active => IsBackdropActive = active);
        placement = new PopupPlacement(this, () => preferredHeight);
        Deactivated += (_, _) => { if (this.dismissOnDeactivate) { dismissRequested = true; ScheduleDismiss(); } };
        Activated += (_, _) => { if (!forceDismissRequested) dismissRequested = false; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { dismissRequested = forceDismissRequested = true; e.Handled = true; TryDismiss(); } };
        Closing += (_, e) => { if (dpiBusy || keepOpenCount > 0) { e.Cancel = true; dismissRequested = forceDismissRequested = true; } };
        Closed += (_, _) => closed = true;
    }
    internal IDisposable KeepOpen()
    {
        keepOpenCount++;
        return new OpenLease(this);
    }
    private sealed class OpenLease : IDisposable
    {
        private QuickWindow? window;
        public OpenLease(QuickWindow window) => this.window = window;
        public void Dispose()
        {
            var current = window; window = null;
            if (current == null) return;
            current.keepOpenCount--; current.ScheduleDismiss();
        }
    }
    private void ScheduleDismiss() => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => TryDismiss()));
    private void TryDismiss()
    {
        if (!closed && dismissRequested && !dpiBusy && keepOpenCount == 0 && (forceDismissRequested || !IsActive)) Close();
    }
    public void PositionNearTray() => placement.Position();
    public void Render()
    {
        // The transaction refreshes display discovery before its task completes. Keep
        // the active controls stable until its owner can render success or failure.
        if (dpiBusy || dpiDragging || keepOpenCount > 0 || activeDpiSlider?.IsKeyboardFocusWithin == true) return;
        body.Children.Clear(); errorViews.Clear();
        foreach (var d in owner.Displays.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool collapsed = owner.Displays.Count > 1 && owner.Preferences.QuickCollapsedDisplays.Contains(d.Id);
            var stack = new StackPanel();
            if (owner.Displays.Count > 1)
            {
                var header = Ui.Button("", () =>
                {
                    if (!owner.Preferences.QuickCollapsedDisplays.Remove(d.Id)) owner.Preferences.QuickCollapsedDisplays.Add(d.Id);
                    owner.SavePreferences(); Render();
                });
                var name = Ui.Text(d.Name, 12, "#303846", true); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = d.Name;
                header.Content = Ui.Row(name, Ui.Text(collapsed ? "▸" : "▾", 11, "#7A8699"));
                header.HorizontalContentAlignment = HorizontalAlignment.Stretch; header.Background = System.Windows.Media.Brushes.Transparent; header.BorderThickness = new Thickness(0); header.Padding = new Thickness(0, 2, 0, 3);
                System.Windows.Automation.AutomationProperties.SetAutomationId(header, "quick-device-header-" + d.Number);
                System.Windows.Automation.AutomationProperties.SetName(header, d.Name + (collapsed ? " 展开" : " 收起"));
                stack.Children.Add(header);
            }
            if (!collapsed)
            {
                stack.Children.Add(owner.CreateSlider("亮度", owner.GetCombinedBrightness(d), 100, async v => { await owner.ChangeCombinedBrightness(d, v); owner.RefreshView(); }, compact: true));
                stack.Children.Add(CreateDpiControls(d));
                bool expanded = owner.Preferences.QuickMoreDisplayId == d.Id;
                if (expanded)
                {
                    var more = new StackPanel();
                    System.Windows.Automation.AutomationProperties.SetAutomationId(more, "quick-more-content-" + d.Number);
                    more.Children.Add(CompactLine());
                    if (d.Modes.Count > 0)
                    {
                        more.Children.Add(CreateModeSection(d, false));
                        more.Children.Add(CreateModeSection(d, true));
                        more.Children.Add(CompactLine());
                    }
                    if (d.Volume.HasValue)
                    {
                        more.Children.Add(owner.CreateSlider("显示器音量", d.Volume, 100, async value =>
                        {
                            await owner.Service.SetPercentAsync(d, 0x62, value); d.Volume = value;
                            owner.Status(d.Name + " · 显示器音量 " + value + "%"); owner.RefreshView();
                        }, compact: true));
                    }
                    if (d.Brightness.HasValue) more.Children.Add(owner.CreateSlider("硬件亮度", d.Brightness, 100, async v => { await owner.ChangeBrightness(d, v); owner.RefreshView(); }, compact: true));
                    more.Children.Add(owner.CreateSlider("额外调暗", d.Dimming, 85, v => { owner.SetDimming(d, v); owner.RefreshView(); return Task.CompletedTask; }, compact: true));
                    more.Children.Add(CompactLine());
                    bool colorExpanded = colorSections.Contains(d.Id);
                    var colorButton = Ui.Button("", () => { if (!colorSections.Remove(d.Id)) colorSections.Add(d.Id); Render(); });
                    colorButton.Content = Ui.Row(Ui.Text("色彩与 HDR", 12), Ui.Text(colorExpanded ? "⌃" : "›", 11, "#7A8699"));
                    colorButton.HorizontalContentAlignment = HorizontalAlignment.Stretch; colorButton.Padding = new Thickness(0, 5, 0, 5); colorButton.Background = System.Windows.Media.Brushes.Transparent; colorButton.BorderThickness = new Thickness(0);
                    System.Windows.Automation.AutomationProperties.SetAutomationId(colorButton, "quick-color-toggle-" + d.Number);
                    more.Children.Add(colorButton);
                    if (colorExpanded)
                    {
                        var colorContent = new Border { Child = owner.CreateColorControls(d, true) };
                        System.Windows.Automation.AutomationProperties.SetAutomationId(colorContent, "quick-color-content-" + d.Number);
                        more.Children.Add(colorContent);
                    }
                    var pip = Ui.Button("", () => owner.OpenPip(d)); pip.Content = Ui.Row(Ui.Text("画中画", 12), Ui.Text("打开  ↗", 11, "#7A8699")); pip.HorizontalContentAlignment = HorizontalAlignment.Stretch; pip.Padding = new Thickness(0, 5, 0, 5); pip.Background = System.Windows.Media.Brushes.Transparent; pip.BorderThickness = new Thickness(0); more.Children.Add(pip);
                    stack.Children.Add(more);
                }
                AddDpiError(stack, d.Id);
                var moreButton = Ui.Button("", () => { owner.Preferences.QuickMoreDisplayId = expanded ? null : d.Id; owner.SavePreferences(); Render(); });
                moreButton.Content = Ui.Icon(expanded ? "\uE70E" : "\uE70D", 12, "#748095"); moreButton.Background = System.Windows.Media.Brushes.Transparent; moreButton.BorderThickness = new Thickness(0); moreButton.Padding = new Thickness(0, 2, 0, 0); moreButton.ToolTip = expanded ? "收起更多选项" : "更多选项";
                System.Windows.Automation.AutomationProperties.SetAutomationId(moreButton, "quick-more-toggle-" + d.Number);
                System.Windows.Automation.AutomationProperties.SetName(moreButton, d.Name + (expanded ? " 收起更多选项" : " 更多选项"));
                stack.Children.Add(moreButton);
            }
            var card = new Border { Child = stack, Padding = new Thickness(12, 7, 12, 7), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Brush("#16000000") }; body.Children.Add(card);
        }
        if (owner.Displays.Count == 0) body.Children.Add(Ui.Card(Ui.Text("未检测到显示器，请打开设置重新检测。", 12, "#8B93A2")));
        var footer = new UniformGrid { Columns = 4, Margin = new Thickness(4, 8, 4, 4) };
        foreach (var item in new (string Label, Action Action)[] { ("设置", owner.ShowSettings), ("排列", owner.ShowLayout), ("虚拟屏幕", owner.ShowVirtualDisplays), ("退出", owner.Quit) })
        {
            var button = Ui.Button(item.Label, item.Action); button.FontSize = 11; button.Padding = new Thickness(4, 6, 4, 6); button.Margin = new Thickness(2, 0, 2, 0); button.Background = Ui.Brush("#70FFFFFF"); button.BorderBrush = Ui.Brush("#12000000"); footer.Children.Add(button);
        }
        body.Children.Add(footer);
        body.Measure(new System.Windows.Size(Width - 18, double.PositiveInfinity));
        preferredHeight = Math.Clamp(body.DesiredSize.Height + 2, 180, 620);
        Height = preferredHeight;
        if (IsVisible) PositionNearTray();
    }
    private static Border CompactLine() { var line = Ui.Line(); line.Margin = new Thickness(0, 4, 0, 4); return line; }
    private UIElement CreateModeSection(DisplayInfo display, bool refreshOnly)
    {
        string sectionId = refreshOnly ? "refresh" : "output";
        bool expanded = modeSections.TryGetValue(display.Id, out var selectedSection) && selectedSection == sectionId;
        var stack = new StackPanel();
        var header = Ui.Button("", () =>
        {
            if (expanded) modeSections.Remove(display.Id); else modeSections[display.Id] = sectionId;
            Render();
        });
        header.Content = Ui.Row(Ui.Text(refreshOnly ? "刷新率" : "输出显示模式", 12), Ui.Text((refreshOnly ? display.Current.Frequency + " Hz" : display.Current.Resolution) + (expanded ? "  ⌃" : "  ›"), 11, "#7A8699"));
        header.HorizontalContentAlignment = HorizontalAlignment.Stretch; header.Padding = new Thickness(0, 5, 0, 5); header.Background = System.Windows.Media.Brushes.Transparent; header.BorderThickness = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(header, "quick-" + sectionId + "-toggle-" + display.Number);
        System.Windows.Automation.AutomationProperties.SetName(header, display.Name + (refreshOnly ? " 刷新率" : " 输出显示模式"));
        stack.Children.Add(header);
        if (!expanded) return stack;
        var modes = refreshOnly
            ? display.Modes.Where(m => m.Width == display.Current.Width && m.Height == display.Current.Height).Distinct().OrderByDescending(m => m.Frequency).ToList()
            : display.Modes.Distinct().ToList();
        var list = new ListBox { ItemsSource = modes, SelectedItem = display.Current, MaxHeight = 210, BorderBrush = Ui.Brush("#DCE2EC"), BorderThickness = new Thickness(1), Background = System.Windows.Media.Brushes.White, FontSize = 11, Padding = new Thickness(3) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "quick-" + sectionId + "-list-" + display.Number);
        System.Windows.Automation.AutomationProperties.SetName(list, display.Name + (refreshOnly ? " 可用刷新率" : " 可用输出模式"));
        if (refreshOnly)
        {
            var template = new DataTemplate(); var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(DisplayMode.Frequency)) { StringFormat = "{0} Hz" }); template.VisualTree = text; list.ItemTemplate = template;
        }
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5))); list.ItemContainerStyle = itemStyle;
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is not DisplayMode chosen || chosen == display.Current || dpiBusy) return;
            await ChangeDisplayAsync(display, () => applyMode(display, chosen, display.Orientation, this));
        };
        list.Loaded += (_, _) => { if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); };
        stack.Children.Add(list);
        var help = Ui.Text(refreshOnly ? "仅列出当前输出分辨率支持的刷新率。" : "选择后直接改变输出像素；放大界面请使用上方的 HiDPI 控制。", 10, "#8790A0"); help.Margin = new Thickness(0, 6, 0, 7); stack.Children.Add(help);
        return stack;
    }
    private async Task ChangeDisplayAsync(DisplayInfo display, Func<Task> operation)
    {
        if (dpiBusy) return;
        dpiBusy = true; body.IsEnabled = false; SetDeviceError(display.Id, null);
        string? error = null;
        try { await operation(); }
        catch (Exception ex) { error = ex.Message; owner.Status(error, true); }
        finally { dpiBusy = false; body.IsEnabled = true; }
        SetDeviceError(display.Id, error);
        Render();
        ScheduleDismiss();
    }
    private UIElement CreateDpiControls(DisplayInfo display)
    {
        var section = new StackPanel();
        System.Windows.Automation.AutomationProperties.SetAutomationId(section, "quick-dpi-" + display.Number);
        var info = display.DpiScale;
        var amount = Ui.Text(info.CanSet ? $"缩放 {info.CurrentPercent}%" : "暂不可用", 11, "#7A8699");
        section.Children.Add(Ui.Row(Ui.Text("分辨率（HiDPI）", 12), amount));
        if (!info.CanSet)
        {
            var help = Ui.Text(info.UnavailableReason, 10, "#8790A0"); help.Margin = new Thickness(0, 3, 0, 3); section.Children.Add(help);
            return section;
        }
        // Moving right increases logical desktop resolution, like the reference
        // Resolution slider. DPI percentages therefore run in descending order.
        var steps = info.SupportedPercents.OrderByDescending(p => p).ToArray();
        var slider = new Slider { Minimum = 0, Maximum = steps.Length - 1, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Value = Array.IndexOf(steps, info.CurrentPercent), Height = 22, Margin = new Thickness(0, 2, 0, 0), IsEnabled = steps.Length > 1 };
        System.Windows.Automation.AutomationProperties.SetName(slider, display.Name + " HiDPI 分辨率");
        System.Windows.Automation.AutomationProperties.SetAutomationId(slider, "quick-dpi-slider-" + display.Number);
        var preview = Ui.Text("", 10, "#64748B"); preview.Margin = new Thickness(0, 1, 0, 1);
        preview.ToolTip = $"输出保持 {display.Current.Resolution} · {display.Current.Frequency} Hz";
        bool resetting = false;
        int SelectedPercent() => steps[Math.Clamp((int)Math.Round(slider.Value), 0, steps.Length - 1)];
        void ResetToCurrent()
        {
            resetting = true; slider.Value = Array.IndexOf(steps, display.DpiScale.CurrentPercent); resetting = false; UpdatePreview();
        }
        void UpdatePreview()
        {
            int percent = SelectedPercent();
            preview.Text = "看起来像约 " + DpiRules.LooksLike(display.Current, percent);
            amount.Text = "缩放 " + percent + "%";
            slider.ToolTip = $"{percent}% · {DpiRules.LooksLike(display.Current, percent)}";
        }
        var writer = new LatestValueWriter(async percent =>
        {
            if (percent != display.DpiScale.CurrentPercent) await applyDpi(display, percent, this);
        }, (percent, error) =>
        {
            SetDeviceError(display.Id, error?.Message);
            if (error != null)
            {
                owner.Status(error.Message, true);
                if (SelectedPercent() == percent && !dpiDragging && !slider.IsMouseCaptureWithin) ResetToCurrent();
            }
        }, delay: () => Task.Delay(60), idle: () =>
        {
            dpiBusy = false; activeDpiDisplay = null;
            if (!dpiDragging && !slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) { activeDpiSlider = null; Render(); }
            ScheduleDismiss();
        });
        void RequestCurrent()
        {
            if (resetting) return;
            if (dpiBusy && activeDpiDisplay != display.Id) { ResetToCurrent(); return; }
            int percent = SelectedPercent();
            if (!writer.IsActive && percent == display.DpiScale.CurrentPercent) return;
            activeDpiDisplay = display.Id; activeDpiSlider = slider; dpiBusy = true;
            writer.Request(percent);
        }
        slider.ValueChanged += (_, _) => { UpdatePreview(); RequestCurrent(); };
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            if (dpiBusy && activeDpiDisplay != display.Id) return;
            activeDpiSlider = slider; dpiDragging = true;
        }));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(async (_, e) =>
        {
            if (activeDpiSlider != slider) return;
            dpiDragging = false;
            if (e.Canceled) { ResetToCurrent(); if (writer.IsActive) writer.Request(SelectedPercent()); }
            else RequestCurrent();
            await writer.WhenIdle;
            activeDpiSlider = null; Render(); ScheduleDismiss();
        }));
        slider.LostKeyboardFocus += (_, _) =>
        {
            if (activeDpiSlider == slider && !dpiDragging && !writer.IsActive) { activeDpiSlider = null; Render(); }
        };
        slider.Unloaded += (_, _) => writer.Stop();
        section.Children.Add(slider);
        section.Children.Add(preview);
        UpdatePreview();
        return section;
    }
    private void AddDpiError(StackPanel section, string id)
    {
        dpiErrors.TryGetValue(id, out var message);
        var feedback = Ui.Text(message ?? "", 11, "#B45136"); feedback.Margin = new Thickness(0, 5, 0, 0); feedback.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        errorViews[id] = feedback;
        section.Children.Add(feedback);
    }
    private void SetDeviceError(string id, string? message)
    {
        if (message == null) dpiErrors.Remove(id); else dpiErrors[id] = message;
        if (errorViews.TryGetValue(id, out var feedback)) { feedback.Text = message ?? ""; feedback.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible; }
    }
}

internal sealed class DimWindow : Window
{
    public DimWindow()
    {
        Title = "LumaDesk · 调光层"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Black; Opacity = 0;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Width = 1; Height = 1;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd, -20, Native.GetWindowLong(hwnd, -20) | 0x20 | 0x80000 | 0x8000000 | 0x80);
        };
    }
    public void Position(DisplayInfo display)
    {
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), display.X, display.Y, (int)display.Current.Width, (int)display.Current.Height, 0x10 | 0x40);
    }
}

internal sealed class PipWindow : Window
{
    private readonly DisplayInfo display;
    private readonly System.Windows.Controls.Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock status = Ui.Text("正在读取画面…", 11, "#9098A8");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool paused;
    public PipWindow(DisplayInfo display)
    {
        this.display = display; Title = display.Name + " · 画中画"; Width = 560; Height = 380; MinWidth = 280; MinHeight = 180; Topmost = true;
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); grid.Children.Add(preview);
        var pause = Ui.Button("暂停 / 继续", () => { paused = !paused; status.Text = paused ? "已暂停" : "实时预览 · 约 6 FPS"; }); pause.FontSize = 11; pause.Padding = new Thickness(10, 5, 10, 5);
        var footer = Ui.Row(status, pause); footer.Margin = new Thickness(12, 4, 12, 4); Grid.SetRow(footer, 1); grid.Children.Add(footer); Content = grid;
        SourceInitialized += (_, _) => Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        timer.Tick += (_, _) => Capture(); Loaded += (_, _) => timer.Start(); Closed += (_, _) => { timer.Stop(); preview.Source = null; };
    }
    private void Capture()
    {
        if (paused || WindowState == WindowState.Minimized) return;
        try
        {
            var current = DisplayService.CurrentMode(display.Device);
            if (current.Width == 0 || current.Height == 0) { status.Text = "显示器已断开"; return; }
            using var full = new Bitmap((int)current.Width, (int)current.Height);
            using (var graphics = Graphics.FromImage(full)) graphics.CopyFromScreen(current.X, current.Y, 0, 0, full.Size);
            int width = Math.Min(960, (int)current.Width); int height = Math.Max(1, (int)(width * current.Height / current.Width));
            using var scaled = new Bitmap(width, height); using (var g = Graphics.FromImage(scaled)) { g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.Bilinear; g.DrawImage(full, 0, 0, width, height); }
            var handle = scaled.GetHbitmap();
            try { var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); preview.Source = source; }
            finally { Native.DeleteObject(handle); }
            status.Text = "实时预览 · SDR · 约 6 FPS";
        }
        catch (Exception ex) { status.Text = "暂时无法捕获此屏幕"; status.ToolTip = ex.Message; }
    }
}

internal static class TrayIcon
{
    public static Drawing.Icon Create()
    {
        using var bitmap = new Bitmap(32, 32); using var g = Graphics.FromImage(bitmap); g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var fill = new Drawing.Drawing2D.LinearGradientBrush(new Drawing.Rectangle(2, 2, 28, 28), Drawing.Color.FromArgb(0, 122, 138), Drawing.Color.FromArgb(53, 86, 190), 45f);
        g.FillEllipse(fill, 1, 1, 30, 30);
        using var pen = new Drawing.Pen(Drawing.Color.White, 3.3f) { StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round, LineJoin = Drawing.Drawing2D.LineJoin.Round };
        g.DrawLines(pen, new[] { new Drawing.PointF(11, 9), new Drawing.PointF(11, 23), new Drawing.PointF(23, 23) });
        using var light = new Drawing.SolidBrush(Drawing.Color.FromArgb(168, 245, 227)); g.FillEllipse(light, 19, 7, 6, 6);
        var handle = bitmap.GetHicon(); try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
    }
}
