// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

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
using System.Threading;

namespace LumaDesk;

internal sealed partial class QuickWindow : Window
{
    private readonly MainWindow owner;
    private readonly Func<DisplayInfo, int, Window, Task> applyDpi;
    private readonly Func<DisplayInfo, DisplayMode, uint, Window, Task> applyMode;
    private readonly Func<DisplayInfo, int, Task> applyBrightness;
    private readonly Func<IReadOnlyList<string>, int, Task> applySelectedBrightness;
    private readonly Dictionary<string, string> modeSections = new();
    private readonly HashSet<string> colorSections = new();
    private bool dpiBusy;
    private string? activeDpiDisplay;
    private Slider? activeDpiSlider;
    private bool dpiDragging;
    private readonly bool dismissOnDeactivate;
    private int keepOpenCount;
    private bool dismissRequested, forceDismissRequested, closed, pinned;
    private readonly PopupPlacement placement;
    private double preferredHeight = 620;
    internal bool IsBackdropActive { get; private set; }
    private readonly Dictionary<string, string> dpiErrors = new();
    private readonly Dictionary<string, TextBlock> errorViews = new();
    private readonly StackPanel body = new() { Margin = new Thickness(6) };
    private readonly StackPanel picker = new() { Margin = new Thickness(18, 12, 18, 0) };
    private readonly ScrollViewer controlsScroll;
    public QuickWindow(MainWindow owner, Func<DisplayInfo, int, Window, Task>? applyDpi = null, Func<DisplayInfo, DisplayMode, uint, Window, Task>? applyMode = null, bool dismissOnDeactivate = true, Func<DisplayInfo, int, Task>? applyBrightness = null, Func<IReadOnlyList<string>, int, Task>? applySelectedBrightness = null)
    {
        Ui.ConfigureWindow(this);
        this.owner = owner; this.applyDpi = applyDpi ?? ((display, percent, parent) => owner.ChangeDpiAsync(display, percent, parent));
        this.applyMode = applyMode ?? ((display, mode, orientation, parent) => owner.ChangeModeAsync(display, mode, orientation, parent));
        this.applyBrightness = applyBrightness ?? (async (display, value) => { await owner.ChangeCombinedBrightness(display, value); owner.RefreshView(); });
        this.applySelectedBrightness = applySelectedBrightness ?? (async (ids, value) => { await owner.ChangeSelectedBrightness(ids, value); owner.RefreshView(); });
        this.dismissOnDeactivate = dismissOnDeactivate;
        Title = AppBrand.Name + L.T("s2c58655791"); Width = 360; Height = 620; MinHeight = 180;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = false; ShowInTaskbar = false; Topmost = true;
        controlsScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(picker); Grid.SetRow(controlsScroll, 1); layout.Children.Add(controlsScroll);
        var surface = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#24000000"), Background = Ui.Brush("#F3F3F3"), Child = layout };
        surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), 12, 12);
        Content = surface; WindowEffects.ConfigurePopup(this, surface, active => IsBackdropActive = active);
        placement = new PopupPlacement(this, () => preferredHeight);
        Deactivated += (_, _) => { if (this.dismissOnDeactivate && !pinned) { dismissRequested = true; ScheduleDismiss(); } };
        Activated += (_, _) => { if (!forceDismissRequested) dismissRequested = false; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { dismissRequested = forceDismissRequested = true; e.Handled = true; TryDismiss(); } };
        Closing += (_, e) => { if (dpiBusy || keepOpenCount > 0) { e.Cancel = true; dismissRequested = forceDismissRequested = true; } };
        Closed += (_, _) => { closed = true; CloseIdentifyWindows(); };
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
        if (!closed && dismissRequested && !dpiBusy && keepOpenCount == 0 && (forceDismissRequested || (!pinned && !IsActive))) Close();
    }
    public void PositionNearTray() => placement.Position();
    public void Render()
    {
        // The transaction refreshes display discovery before its task completes. Keep
        // the active controls stable until its owner can render success or failure.
        if (!CanRebuild) return;
        body.Children.Clear(); picker.Children.Clear(); errorViews.Clear(); multiLevelLabels.Clear();
        var selected = ResolveSelectedDisplay();
        multiSelection.IntersectWith(owner.Displays.Select(d => d.Id));
        RenderDisplayPicker(selected);
        if (brightnessMultiSelect)
        {
            body.Children.Add(new Border { Child = CreateMultiBrightnessControls(), Padding = new Thickness(12, 7, 12, 12) });
            var scope = Ui.Text(L.T("quick.multiScope"), 11, "#7A8699");
            scope.TextWrapping = TextWrapping.Wrap; scope.Margin = new Thickness(12, 5, 12, 10);
            body.Children.Add(scope);
        }
        else if (selected is { } d)
        {
            var stack = new StackPanel();
            stack.Children.Add(owner.CreateSlider(L.T("s8e2f643ad1"), owner.GetCombinedBrightness(d), 100, v => this.applyBrightness(d, v), compact: true));
            if (owner.Options(d).ShowScaling) stack.Children.Add(CreateDpiControls(d));
            bool expanded = owner.Preferences.QuickMoreDisplayId != null;
            AddDpiError(stack, d.Id);
            var moreButton = Ui.Button("", () =>
            {
                if (!CanRebuild) return;
                owner.Preferences.QuickMoreDisplayId = expanded ? null : d.Id;
                owner.SavePreferences(); Render();
            });
            moreButton.Content = Ui.Row(Ui.Text(L.T(expanded ? "quick.less" : "quick.more"), 12), Ui.Icon(expanded ? "\uE70E" : "\uE70D", 12, "#748095"));
            moreButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            moreButton.Background = System.Windows.Media.Brushes.Transparent; moreButton.BorderThickness = new Thickness(0);
            moreButton.Padding = new Thickness(0, 8, 0, 8);
            System.Windows.Automation.AutomationProperties.SetAutomationId(moreButton, "quick-more-toggle-" + d.Number);
            System.Windows.Automation.AutomationProperties.SetName(moreButton, L.T(expanded ? "quick.less" : "quick.more"));
            stack.Children.Add(CompactLine()); stack.Children.Add(moreButton);
            if (expanded)
            {
                var more = new StackPanel();
                more.Children.Add(owner.CreateInputControls(d));
                System.Windows.Automation.AutomationProperties.SetAutomationId(more, "quick-more-content-" + d.Number);
                more.Children.Add(CompactLine());
                if (d.Modes.Count > 0)
                {
                    more.Children.Add(CreateModeSection(d, false));
                    more.Children.Add(CreateModeSection(d, true));
                    more.Children.Add(CreateRotationControl(d));
                    more.Children.Add(CompactLine());
                }
                if (d.Volume.HasValue)
                {
                    more.Children.Add(owner.CreateSlider(L.T("s716ddda53d"), d.Volume, 100, async value =>
                    {
                        await owner.Service.SetPercentAsync(d, 0x62, value); d.Volume = value;
                        owner.Status(d.Name + L.T("s5caf7d72ba") + value + "%"); owner.RefreshView();
                    }, compact: true));
                }
                if (d.Brightness.HasValue) more.Children.Add(owner.CreateSlider(L.T("s06dab760bb"), d.Brightness, 100, async v => { await owner.ChangeBrightness(d, v); owner.RefreshView(); }, compact: true));
                if (d.Contrast.HasValue) more.Children.Add(owner.CreateSlider(L.T("s4cbc753240"), d.Contrast, 100, async value =>
                {
                    await owner.Service.SetPercentAsync(d, 0x12, value); d.Contrast = value; owner.RefreshView();
                }, compact: true));
                more.Children.Add(owner.CreateSlider(L.T("s417de11214"), d.Dimming, 85, v => { owner.SetDimming(d, v); owner.RefreshView(); return Task.CompletedTask; }, compact: true));
                more.Children.Add(CompactLine());
                bool colorExpanded = colorSections.Contains(d.Id);
                var colorButton = Ui.Button("", () => { if (!colorSections.Remove(d.Id)) colorSections.Add(d.Id); Render(); });
                colorButton.Content = Ui.Row(Ui.Text(L.T("s58169370ac"), 12), Ui.Text(colorExpanded ? "⌃" : "›", 11, "#7A8699"));
                colorButton.HorizontalContentAlignment = HorizontalAlignment.Stretch; colorButton.Padding = new Thickness(0, 5, 0, 5); colorButton.Background = System.Windows.Media.Brushes.Transparent; colorButton.BorderThickness = new Thickness(0);
                System.Windows.Automation.AutomationProperties.SetAutomationId(colorButton, "quick-color-toggle-" + d.Number);
                more.Children.Add(colorButton);
                if (colorExpanded)
                {
                    var colorContent = new Border { Child = owner.CreateColorControls(d, true) };
                    System.Windows.Automation.AutomationProperties.SetAutomationId(colorContent, "quick-color-content-" + d.Number);
                    more.Children.Add(colorContent);
                }
                var pip = Ui.Button("", () => owner.OpenPip(d)); pip.Content = Ui.Row(Ui.Text(L.T("s850875a753"), 12), Ui.Text(L.T("s44429022e6"), 11, "#7A8699")); pip.HorizontalContentAlignment = HorizontalAlignment.Stretch; pip.Padding = new Thickness(0, 5, 0, 5); pip.Background = System.Windows.Media.Brushes.Transparent; pip.BorderThickness = new Thickness(0); more.Children.Add(pip);
                stack.Children.Add(more);
            }
            var card = new Border { Child = stack, Padding = new Thickness(12, 7, 12, 7), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Ui.Brush("#16000000") }; body.Children.Add(card);
        }
        if (!brightnessMultiSelect && owner.Displays.Count == 0) body.Children.Add(Ui.Card(Ui.Text(L.T("s7d06b98afa"), 12, "#8B93A2")));
        if (!brightnessMultiSelect && owner.Preferences.Presets.Count > 0) { var scenes = owner.SceneMenuButton(); scenes.Margin = new Thickness(8, 6, 8, 0); body.Children.Add(scenes); }
        var footer = new UniformGrid { Columns = 4, Margin = new Thickness(4, 8, 4, 4) };
        foreach (var item in new (string Label, Action Action)[] { (L.T("sdf3d58c7d8"), owner.ShowSettings), (L.T("s70473d8ed9"), owner.ShowLayout), (L.T("quick.virtual"), owner.ShowVirtualDisplays), (pinned ? L.T("panel.unpin") : L.T("panel.pin"), () => { pinned = !pinned; Render(); }) })
        {
            var button = Ui.Button(item.Label, item.Action); button.FontSize = 11; button.Padding = new Thickness(4, 6, 4, 6); button.Margin = new Thickness(2, 0, 2, 0); button.Background = Ui.Brush("#70FFFFFF"); button.BorderBrush = Ui.Brush("#12000000"); footer.Children.Add(button);
        }
        if (!brightnessMultiSelect) body.Children.Add(footer);
        body.Measure(new System.Windows.Size(Width - 2, double.PositiveInfinity));
        picker.Measure(new System.Windows.Size(Width - 2, double.PositiveInfinity));
        // Native pixel rounding at fractional DPI can otherwise create a scrollbar
        // for a fraction of one DIP even when all the controls fit.
        preferredHeight = Math.Clamp(Math.Ceiling(body.DesiredSize.Height + picker.DesiredSize.Height + 2) + 1, 180, 620);
        Height = preferredHeight;
        if (IsVisible) PositionNearTray();
    }
    private static Border CompactLine() { var line = Ui.Line(); line.Margin = new Thickness(0, 4, 0, 4); return line; }
    private UIElement CreateRotationControl(DisplayInfo display)
    {
        var rotation = new ComboBox
        {
            ItemsSource = new[] { L.T("s35294d7f91"), L.T("se6121d2fdf"), L.T("sbc911c8ebe"), L.T("sf6f2dc11c9") },
            SelectedIndex = (int)display.Orientation, MinWidth = 0, Width = 160, FontSize = 11,
            Margin = new Thickness(0, 5, 0, 5)
        };
        System.Windows.Automation.AutomationProperties.SetName(rotation, L.T("s60cc99cc00"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(rotation, "quick-rotation-" + display.Number);
        rotation.SelectionChanged += async (_, _) =>
        {
            if (rotation.SelectedIndex < 0 || (uint)rotation.SelectedIndex == display.Orientation) return;
            uint orientation = (uint)rotation.SelectedIndex;
            if (dpiBusy) { rotation.SelectedIndex = (int)display.Orientation; return; }
            await ChangeDisplayAsync(display, () => applyMode(display, display.Current, orientation, this));
        };
        return Ui.Row(Ui.Text(L.T("s60cc99cc00"), 12), rotation);
    }
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
        header.Content = Ui.Row(Ui.Text(refreshOnly ? L.T("sf48164ea8a") : L.T("sbe69f5dbee"), 12), Ui.Text((refreshOnly ? display.Current.Frequency + " Hz" : display.Current.Resolution) + (expanded ? "  ⌃" : "  ›"), 11, "#7A8699"));
        header.HorizontalContentAlignment = HorizontalAlignment.Stretch; header.Padding = new Thickness(0, 5, 0, 5); header.Background = System.Windows.Media.Brushes.Transparent; header.BorderThickness = new Thickness(0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(header, "quick-" + sectionId + "-toggle-" + display.Number);
        System.Windows.Automation.AutomationProperties.SetName(header, display.Name + (refreshOnly ? L.T("s27a6f7c7a1") : L.T("se287dc7c0f")));
        stack.Children.Add(header);
        if (!expanded) return stack;
        var modes = refreshOnly
            ? display.Modes.Where(m => m.Width == display.Current.Width && m.Height == display.Current.Height).Distinct().OrderByDescending(m => m.Frequency).ToList()
            : display.Modes.Distinct().ToList();
        var list = new ListBox { ItemsSource = modes, SelectedItem = display.Current, MaxHeight = 210, BorderBrush = Ui.Brush("#DCE2EC"), BorderThickness = new Thickness(1), Background = Ui.Brush("#FFFFFF"), Foreground = ThemeService.Brush("#242424", true), FontSize = 11, Padding = new Thickness(3) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "quick-" + sectionId + "-list-" + display.Number);
        System.Windows.Automation.AutomationProperties.SetName(list, display.Name + (refreshOnly ? L.T("s0e76452bcb") : L.T("s56e1e2e177")));
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
        if (!refreshOnly)
        {
            var favorites = owner.Options(display).Favorites;
            var star = Ui.Button(favorites.Contains(display.Current) ? L.T("mode.unfavorite") : L.T("mode.favorite"), () =>
            {
                if (!favorites.Remove(display.Current)) favorites.Add(display.Current);
                owner.SavePreferences(); Render();
            });
            star.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(star);
            foreach (var mode in favorites.Distinct().ToArray())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                var pick = Ui.Button("★ " + mode, () => _ = ChangeDisplayAsync(display, () => applyMode(display, mode, display.Orientation, this)));
                pick.IsEnabled = display.Modes.Contains(mode); if (!pick.IsEnabled) pick.ToolTip = L.T("mode.unavailable");
                var remove = Ui.Button("×", () => { favorites.Remove(mode); owner.SavePreferences(); Render(); });
                System.Windows.Automation.AutomationProperties.SetName(remove, L.T("mode.unfavorite") + " " + mode);
                row.Children.Add(pick); row.Children.Add(remove); stack.Children.Add(row);
            }
        }
        var help = Ui.Text(refreshOnly ? L.T("s82f9d30470") : L.T("s29dce845a6"), 10, "#8790A0"); help.Margin = new Thickness(0, 6, 0, 7); stack.Children.Add(help);
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
        var amount = Ui.Text(info.CanSet ? L.F("sdaee699fa0", info.CurrentPercent) : L.T("sf36cac9622"), 11, "#7A8699");
        section.Children.Add(Ui.Row(Ui.Text(L.T("scene.scale"), 12), amount));
        if (!info.CanSet)
        {
            var help = Ui.Text(info.UnavailableReason, 10, "#8790A0"); help.Margin = new Thickness(0, 3, 0, 3); section.Children.Add(help);
            return section;
        }
        // Moving right increases logical desktop resolution, like the reference
        // Resolution slider. DPI percentages therefore run in descending order.
        var steps = info.SupportedPercents.OrderByDescending(p => p).ToArray();
        var slider = new Slider { Minimum = 0, Maximum = steps.Length - 1, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Value = Array.IndexOf(steps, info.CurrentPercent), Height = 22, Margin = new Thickness(0, 2, 0, 0), IsEnabled = steps.Length > 1 };
        System.Windows.Automation.AutomationProperties.SetName(slider, display.Name + L.T("s4fa3927f98"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(slider, "quick-dpi-slider-" + display.Number);
        var preview = Ui.Text("", 10, "#64748B"); preview.Margin = new Thickness(0, 1, 0, 1);
        preview.ToolTip = L.F("se4551ed916", display.Current.Resolution, display.Current.Frequency);
        bool resetting = false;
        int SelectedPercent() => steps[Math.Clamp((int)Math.Round(slider.Value), 0, steps.Length - 1)];
        void ResetToCurrent()
        {
            resetting = true; slider.Value = Array.IndexOf(steps, display.DpiScale.CurrentPercent); resetting = false; UpdatePreview();
        }
        void UpdatePreview()
        {
            int percent = SelectedPercent();
            preview.Text = L.T("s8673626e8f") + DpiRules.LooksLike(display.Current, percent);
            amount.Text = L.T("se726470b6d") + percent + "%";
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
        Title = L.T("s48a7629fcc"); WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Black; Opacity = 0;
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
    private readonly TextBlock status = Ui.Text(L.T("sa10706fb30"), 11, "#9098A8");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool paused, capturing, closed, selecting;
    private Bitmap? full, scaled;
    private Drawing.Rectangle? crop;
    private System.Windows.Point? selectionStart;
    private readonly Border selection = new() { BorderBrush = System.Windows.Media.Brushes.DodgerBlue, BorderThickness = new Thickness(2), Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 0, 120, 255)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Canvas selectionLayer = new() { IsHitTestVisible = false };
    private Drawing.Rectangle lastArea;
    internal bool Capturing => capturing;
    public PipWindow(DisplayInfo display)
    {
        Ui.ConfigureWindow(this);
        this.display = display; Title = display.Name + L.T("s20bd7e7897"); Width = 560; Height = 380; MinWidth = 360; MinHeight = 180; Topmost = true;
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        var view = new Grid { Background = System.Windows.Media.Brushes.Transparent }; view.Children.Add(preview); selectionLayer.Children.Add(selection); view.Children.Add(selectionLayer); grid.Children.Add(view);
        var pause = Ui.Button(L.T("s6b0fa0abdc"), () => { paused = !paused; status.Text = paused ? L.T("seb0c326b60") : L.T("s9f277b6ebd"); });
        var region = Ui.Button(L.T("pip.region"), () => { selecting = !selecting; view.Cursor = selecting ? Cursors.Cross : Cursors.Arrow; status.Text = L.T("pip.drag"); });
        var reset = Ui.Button(L.T("pip.full"), () => { crop = null; selecting = false; view.Cursor = Cursors.Arrow; });
        var fps = new ComboBox { ItemsSource = new[] { "6 FPS", "15 FPS", "30 FPS" }, SelectedIndex = 0, MinWidth = 75, FontSize = 11 };
        fps.SelectionChanged += (_, _) => timer.Interval = TimeSpan.FromMilliseconds(fps.SelectedIndex == 2 ? 33 : fps.SelectedIndex == 1 ? 67 : 150);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
        foreach (var button in new[] { pause, region, reset }) { button.FontSize = 11; button.Padding = new Thickness(8, 5, 8, 5); footer.Children.Add(button); }
        footer.Children.Add(fps); status.Margin = new Thickness(8, 0, 0, 0); status.MaxWidth = 120; status.TextTrimming = TextTrimming.CharacterEllipsis; status.TextWrapping = TextWrapping.NoWrap; footer.Children.Add(status);
        Grid.SetRow(footer, 1); grid.Children.Add(footer); Content = grid;
        view.MouseLeftButtonDown += (_, e) =>
        {
            if (!selecting || preview.Source == null) return;
            selectionStart = e.GetPosition(view); view.CaptureMouse(); Canvas.SetLeft(selection, selectionStart.Value.X); Canvas.SetTop(selection, selectionStart.Value.Y); selection.Width = selection.Height = 0; selection.Visibility = Visibility.Visible;
        };
        view.MouseMove += (_, e) =>
        {
            if (selectionStart is not { } start) return;
            var end = e.GetPosition(view); Canvas.SetLeft(selection, Math.Min(start.X, end.X)); Canvas.SetTop(selection, Math.Min(start.Y, end.Y)); selection.Width = Math.Abs(end.X - start.X); selection.Height = Math.Abs(end.Y - start.Y);
        };
        view.MouseLeftButtonUp += (_, e) =>
        {
            if (selectionStart is not { } start) return;
            var end = e.GetPosition(view); selectionStart = null; selection.Visibility = Visibility.Collapsed; view.ReleaseMouseCapture();
            var area = lastArea;
            if (area.Width <= 0 || area.Height <= 0) return;
            double scale = Math.Min(view.ActualWidth / area.Width, view.ActualHeight / area.Height);
            double left = (view.ActualWidth - area.Width * scale) / 2, top = (view.ActualHeight - area.Height * scale) / 2;
            int x = (int)Math.Clamp((Math.Min(start.X, end.X) - left) / scale, 0, area.Width);
            int y = (int)Math.Clamp((Math.Min(start.Y, end.Y) - top) / scale, 0, area.Height);
            int right = (int)Math.Clamp((Math.Max(start.X, end.X) - left) / scale, 0, area.Width);
            int bottom = (int)Math.Clamp((Math.Max(start.Y, end.Y) - top) / scale, 0, area.Height);
            if (right - x >= 16 && bottom - y >= 16) crop = new Drawing.Rectangle(area.X + x, area.Y + y, right - x, bottom - y);
            selecting = false; view.Cursor = Cursors.Arrow;
        };
        SourceInitialized += (_, _) => Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        timer.Tick += async (_, _) => await CaptureAsync(); Loaded += (_, _) => timer.Start();
        Closed += (_, _) => { closed = true; timer.Stop(); preview.Source = null; if (!capturing) DisposeBuffers(); };
    }
    private void DisposeBuffers() { full?.Dispose(); full = null; scaled?.Dispose(); scaled = null; }
    private async Task CaptureAsync()
    {
        if (paused || capturing || closed || selectionStart != null || WindowState == WindowState.Minimized) return;
        capturing = true; var requestedCrop = crop;
        try
        {
            var frame = await Task.Run(() =>
            {
                DisplayService.ValidateIdentity(display.Device, display.Id);
                var current = DisplayService.CurrentMode(display.Device);
                var area = new Drawing.Rectangle(0, 0, (int)current.Width, (int)current.Height);
                if (requestedCrop.HasValue) area = Drawing.Rectangle.Intersect(area, requestedCrop.Value);
                if (area.Width <= 0 || area.Height <= 0) throw new InvalidOperationException(L.T("s88561e63c9"));
                if (full == null || full.Width != area.Width || full.Height != area.Height) { full?.Dispose(); full = new Bitmap(area.Width, area.Height); }
                using (var graphics = Graphics.FromImage(full)) graphics.CopyFromScreen(current.X + area.X, current.Y + area.Y, 0, 0, full.Size);
                int width = Math.Min(960, area.Width), height = Math.Max(1, width * area.Height / area.Width);
                if (scaled == null || scaled.Width != width || scaled.Height != height) { scaled?.Dispose(); scaled = new Bitmap(width, height); }
                using (var g = Graphics.FromImage(scaled)) { g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.Bilinear; g.DrawImage(full, 0, 0, width, height); }
                var handle = scaled.GetHbitmap();
                try { var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); return (Source: source, Area: area); }
                finally { Native.DeleteObject(handle); }
            });
            if (!closed) { preview.Source = frame.Source; lastArea = frame.Area; if (!selecting) status.Text = L.T("s1bbc67943d"); }
        }
        catch (Exception ex) { if (!closed) { status.Text = L.T("se2c5e0ec0d"); status.ToolTip = ex.Message; } }
        finally { capturing = false; if (closed) DisposeBuffers(); }
    }
}
