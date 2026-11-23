// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

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
    private bool compactNavigation;
    private readonly List<PipWindow> pipWindows = new();
    private readonly DispatcherTimer hotplugTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Forms.NotifyIcon? tray;
    private bool lightTrayGlyph;
    private QuickWindow? quick;
    private string page = "displays";
    private string? selectedId;
    private bool scanning, exiting, modePending;
    private TaskCompletionSource? brightnessScan;
    private IntPtr hwnd;
    internal DisplayInfo? Selected => Displays.FirstOrDefault(d => d.Id == selectedId) ?? Displays.FirstOrDefault();

    private readonly Func<bool>? fixtureSettingsVisible;

    public MainWindow() : this(null) { }

    internal MainWindow(Func<bool>? fixtureSettingsVisible)
    {
        this.fixtureSettingsVisible = fixtureSettingsVisible;
        ThemeService.Apply(Preferences.Theme);
        InitializeComponent();
        Ui.ConfigureWindow(this);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(FitToMonitor);
        AddNavigation("displays", L.T("s1a13ea5910"), "\uE7F4"); AddNavigation("groups", L.T("s563a1990f3"), "\uE8A9"); AddNavigation("application", L.T("s63c73c4730"), "\uE713"); AddNavigation("quick", L.T("s770544f702"), "\uE8A5"); AddNavigation("keyboard", L.T("s864200bbb2"), "\uE765"); AddNavigation("presets", L.T("sf1de1e2621"), "\uE734");
        ShellSurface.SizeChanged += (_, e) => UpdateSidebar(e.NewSize.Width < 800);
        hotplugTimer.Tick += async (_, _) => { hotplugTimer.Stop(); await RestoreAfterConnectionAsync(); };
        Loaded += async (_, _) => { FitToMonitor(); SetupTray(); await RefreshAsync(); StartAutomation(); StartCommands(); };
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
            if (modePending || sceneBusy) { e.Cancel = true; return; }
            FinishAutoUpdates();
            ShutdownResources();
        };
    }
    private void AddNavigation(string key, string name, string icon)
    {
        var button = Ui.Button(name, () => Navigate(key));
        button.Style = (Style)FindResource("Nav"); button.Content = Ui.IconLabel(icon, name); button.ToolTip = name; button.Tag = name; navButtons[key] = button; Navigation.Children.Add(button);
    }
    private void UpdateSidebar(bool compact)
    {
        if (compact == compactNavigation) return;
        compactNavigation = compact;
        SidebarColumn.Width = new GridLength(compact ? 68 : 196);
        BrandName.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Breadcrumb.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        Brand.Margin = new Thickness(compact ? 19 : 22, 0, 0, 0);
        QuickLauncher.Content = compact ? Ui.Icon("\uE8A5", 16) : L.T("saf91a997fb");
        foreach (var button in navButtons.Values)
        {
            var content = (Grid)button.Content;
            content.Children[1].Visibility = BrandName.Visibility;
            button.Padding = new Thickness(compact ? 7 : 12, 11, compact ? 7 : 12, 11);
        }
    }
    private void Navigate(string destination, string? displayId = null)
    {
        page = destination;
        selectedId = displayId;
        Render();
        ContentScroll.ScrollToTop();
    }
    private void FitToMonitor()
    {
        if (hwnd == IntPtr.Zero || WindowState != WindowState.Normal) return;
        var work = Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double availableWidth = Math.Max(320, work.Width / dpi.DpiScaleX - 16), availableHeight = Math.Max(280, work.Height / dpi.DpiScaleY - 16);
        MinWidth = Math.Min(680, availableWidth); MinHeight = Math.Min(540, availableHeight);
        Width = Math.Min(Width, availableWidth); Height = Math.Min(Height, availableHeight);
    }
    internal void Status(string message, bool error = false) { StatusLabel.ToolTip = message; StatusLabel.Text = message; StatusLabel.Foreground = ThemeService.Brush(error ? "#B45136" : "#777F8E", true); Storage.Log(message); }
    internal async Task RunAsync(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) { Status(ex.Message, true); MessageBox.Show(this, ex.Message, L.T("s79be13bdf0"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    internal async Task RefreshAsync(bool sceneRefresh = false)
    {
        if (scanning || modePending || (sceneBusy && !sceneRefresh)) return;
        ResetIdleDimming();
        scanning = true;
        brightnessScan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Status(L.T("s117b82fa19"));
        try
        {
            // Let an in-flight brightness write finish before replacing its display snapshot.
            await Task.WhenAll(brightnessGates.Values.ToArray().Select(async gate =>
            {
                await gate.WaitAsync();
                gate.Release();
            }));
            var next = await Service.ScanAsync();
            foreach (var display in next) { display.Dimming = Displays.FirstOrDefault(d => d.Id == display.Id)?.Dimming ?? 0; ApplyAlias(display); }
            Displays = next;
            foreach (var id in dimWindows.Keys.ToArray())
            {
                var display = Displays.FirstOrDefault(d => d.Id == id);
                if (display == null) { dimWindows[id].Close(); dimWindows.Remove(id); }
                else dimWindows[id].Position(display);
            }
            foreach (var display in next) BrightnessChanged?.Invoke(display);
            if (!testing) UpdateNightDimming(DateTime.Now, force: true);
            Render(); quick?.Render(); Status(L.F("sd1362bfd3f", Displays.Count, DateTime.Now));
        }
        catch (Exception ex) { Status(L.T("sf7293a061e") + ex.Message, true); Render(); }
        finally { scanning = false; brightnessScan.SetResult(); brightnessScan = null; }
    }
    private void Render()
    {
        // Tray startup and background refreshes only need the display model, not the settings UI.
        if (!(fixtureSettingsVisible?.Invoke() ?? IsVisible)) return;
        if (selectedId != null && !Displays.Any(d => d.Id == selectedId)) selectedId = null;
        foreach (var pair in navButtons)
        {
            bool current = pair.Key == page;
            pair.Value.Background = Ui.Brush(current ? "#E4F2F3" : "#00FFFFFF");
            pair.Value.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetItemStatus(pair.Value, current ? L.T("quick.selected") : "");
        }
        Breadcrumb.Text = L.T("s424b6d3b48") + (navButtons.TryGetValue(page, out var navigation) ? (string)navigation.Tag : page);
        PageContent.Children.Clear(); DisplayTabs.Children.Clear();
        TabStrip.Visibility = page == "displays" && selectedId != null ? Visibility.Visible : Visibility.Collapsed;
        if (TabStrip.Visibility == Visibility.Visible) BuildTabs();
        switch (page)
        {
            case "displays": if (selectedId != null && Selected != null) RenderDisplay(Selected); else RenderOverview(); break;
            case "groups": RenderGroups(); break;
            case "application": RenderApplication(); break;
            case "quick": RenderMenu(); break;
            case "keyboard": RenderKeyboard(); break;
            case "presets": RenderPresets(); break;
        }
    }
    internal void RefreshView() => Render();
    private void BuildTabs()
    {
        AddTab(L.T("sfea405f9b0"), "\uE72B", false, () => Navigate("displays"));
        foreach (var display in Displays)
        {
            AddTab(display.Name, "\uE7F4", selectedId == display.Id, () => Navigate("displays", display.Id));
        }
    }
    private void AddTab(string title, string icon, bool active, Action action)
    {
        var button = Ui.Button(title, action);
        var name = Ui.Text(title, 12, active ? "#005C68" : "#515B6A", active);
        name.MaxWidth = 144; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        button.Content = Ui.Row(Ui.Icon(icon, 14), name, 6);
        button.ToolTip = title; button.Margin = new Thickness(0, 0, 6, 0);
        button.Background = Ui.Brush(active ? "#E4F2F3" : "#FFFFFF");
        button.BorderBrush = Ui.Brush(active ? "#007A8A" : "#D9D9D9");
        if (active) button.Loaded += (_, _) => button.BringIntoView();
        DisplayTabs.Children.Add(button);
    }
    private void Heading(string title, string? hint = null, string? detail = null)
    {
        var heading = Ui.Hint(Ui.Text(title, 26, "#242A35", true), hint);
        if (hint != null && navButtons.TryGetValue(page, out var nav)) Ui.Hint(nav, nav.Tag + "\n" + hint);
        heading.Margin = new Thickness(0, 0, 0, detail == null ? 16 : 4);
        PageContent.Children.Add(heading);
        if (detail != null) { var sub = Ui.Text(detail, 13, "#606977"); sub.Margin = new Thickness(0, 0, 0, 18); PageContent.Children.Add(sub); }
    }
    private string? Help(string text) => Preferences.ShowHelp ? text : null;
    private void RenderOverview()
    {
        var head = Ui.Row(Ui.Text(L.T("s20ccee3b80"), 26, bold: true), Ui.IconButton("\uE72C", L.T("sa13550662f"), () => _ = RefreshAsync()));
        head.Margin = new Thickness(0, 0, 0, 16); PageContent.Children.Add(head);
        if (Displays.Count == 0) PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("sdc39a81d49"), L.T("sfd1e46671c"))));
        else
        {
            var list = new StackPanel();
            foreach (var d in Displays)
            {
                if (list.Children.Count > 0) list.Children.Add(Ui.Line());
                var left = new Grid(); left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); left.ColumnDefinitions.Add(new ColumnDefinition());
                var icon = new Border { Child = Ui.Icon("\uE7F4", 25, "#FFFFFF"), Background = Ui.Brush("#007A8A"), CornerRadius = new CornerRadius(10), Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Left }; left.Children.Add(icon);
                var labels = Ui.Labels(d.Name, d.Subtitle); Grid.SetColumn(labels, 1); left.Children.Add(labels);
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                if (d.Primary) actions.Children.Add(Ui.Badge(L.T("s93ff5822ca")));
                var arrow = Ui.Icon("\uE76C", 12, "#616161"); arrow.Margin = new Thickness(14, 0, 0, 0); actions.Children.Add(arrow);
                var open = Ui.Button(L.T("s84041df0ce") + d.Name, () => Navigate("displays", d.Id));
                open.Content = Ui.Row(left, actions); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                open.Padding = new Thickness(8); open.Background = Brushes.Transparent; open.BorderThickness = new Thickness(0);
                list.Children.Add(open);
            }
            PageContent.Children.Add(Ui.Card(list));
            var layout = new StackPanel(); layout.Children.Add(Ui.Row(Ui.IconLabel("\uE8A9", L.T("sa6d9aa75a3")), Ui.IconButton("\uE70F", L.T("s7c7d6a8555"), ShowLayout)));
            var canvas = new Canvas { Height = 132, Margin = new Thickness(0, 12, 0, 2), ClipToBounds = true };
            canvas.SizeChanged += (_, _) => DrawArrangement(canvas); layout.Children.Add(canvas);
            PageContent.Children.Add(Ui.Card(layout));
        }
        var manage = Ui.Button(L.T("sf3b6083958"), ShowVirtualDisplays);
        manage.Content = Ui.Row(Ui.IconLabel("\uE7F8", L.T("s1b4f0e57f8")), Ui.Icon("\uE76C", 12));
        manage.HorizontalContentAlignment = HorizontalAlignment.Stretch; manage.Padding = new Thickness(16);
        manage.Margin = new Thickness(0, 0, 0, 8);
        PageContent.Children.Add(Ui.Hint(manage, L.T("s4d57c168c5")));
    }
    private void DrawArrangement(Canvas canvas)
    {
        canvas.Children.Clear(); if (Displays.Count == 0 || canvas.ActualWidth <= 0) return;
        foreach (var tile in QuickDisplayMap.Project(Displays, canvas.ActualWidth, canvas.Height))
        {
            var d = tile.Display;
            bool compact = tile.Bounds.Height < 50;
            var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Ui.Text(d.Number.ToString(), compact ? 18 : 24, d.Primary ? "#006A78" : "#515B6A", true); number.HorizontalAlignment = HorizontalAlignment.Center; inner.Children.Add(number);
            var label = Ui.Text(d.Name, 11, "#576774"); label.TextTrimming = TextTrimming.CharacterEllipsis; label.TextWrapping = TextWrapping.NoWrap; label.HorizontalAlignment = HorizontalAlignment.Center; if (!compact) inner.Children.Add(label);
            var screen = Ui.Button(d.Name, () => Navigate("displays", d.Id));
            screen.Content = inner; screen.Width = tile.Bounds.Width; screen.Height = tile.Bounds.Height;
            screen.Padding = new Thickness(2); screen.Background = Ui.Brush(d.Primary ? "#E4F2F3" : "#F3F3F3");
            screen.BorderBrush = Ui.Brush(d.Primary ? "#007A8A" : "#B8B8B8"); screen.ToolTip = L.F("s6e3563d6e6", d.Name, d.Subtitle, d.X, d.Y);
            Canvas.SetLeft(screen, tile.Bounds.Left); Canvas.SetTop(screen, tile.Bounds.Top); canvas.Children.Add(screen);
        }
    }
    private void RenderDisplay(DisplayInfo display)
    {
        Heading(display.Name, detail: display.Subtitle + (display.Primary ? L.T("s28102bcda6") : ""));
        PageContent.Children.Add(Ui.Card(CreateDisplayResetControls(display)));
        var method = display.HasDdc ? L.T("s77e64220a2") : display.HasWmi ? L.T("s16cc598b36") : L.T("sa81ff06ccd");
        var light = Ui.Hint(new StackPanel(), method);
        if (display.Contrast.HasValue || display.Volume.HasValue)
        {
            light.Children.Add(Ui.Row(Ui.IconLabel("\uE706", L.T("s437926dd65")), Ui.Badge(method)));
            light.Children.Add(Ui.Line());
        }
        light.Children.Add(CreateBrightnessSlider(display, ChangeCombinedBrightness));
        if (display.Contrast.HasValue) { light.Children.Add(Ui.Line()); light.Children.Add(CreateSlider(L.T("s4cbc753240"), display.Contrast, 100, async value => { await Service.SetPercentAsync(display, 0x12, value); display.Contrast = value; Status(L.T("s1083ba917f")); })); }
        if (display.Volume.HasValue) { light.Children.Add(Ui.Line()); light.Children.Add(CreateSlider(L.T("s716ddda53d"), display.Volume, 100, async value => { await Service.SetPercentAsync(display, 0x62, value); display.Volume = value; Status(L.T("sd3fbc328d7")); })); }
        PageContent.Children.Add(Ui.Card(light));
        RenderDpiScaling(display);
        var mode = new StackPanel(); mode.Children.Add(Ui.IconLabel("\uE7F4", L.T("sdfc8b14cad"))); mode.Children.Add(Ui.Line());
        var resolutions = display.Modes.Select(m => m.Resolution).Distinct().ToList();
        var resolution = new ComboBox { ItemsSource = resolutions, SelectedItem = display.Current.Resolution, Width = 185 };
        var refresh = new ComboBox { Width = 185 }; var orientation = new ComboBox { ItemsSource = new[] { L.T("s35294d7f91"), L.T("se6121d2fdf"), L.T("sbc911c8ebe"), L.T("sf6f2dc11c9") }, SelectedIndex = (int)display.Orientation, Width = 185 };
        void UpdateRates() { var rates = display.Modes.Where(m => m.Resolution == (string?)resolution.SelectedItem).Select(m => m.Frequency).Distinct().ToList(); refresh.ItemsSource = rates; refresh.SelectedItem = rates.Contains(display.Current.Frequency) ? display.Current.Frequency : rates.FirstOrDefault(); }
        UpdateRates(); resolution.SelectionChanged += (_, _) => UpdateRates();
        mode.Children.Add(Ui.Setting(L.T("s248a33b7b3"), Help(L.T("s2b8ac41596")), resolution)); mode.Children.Add(Ui.Line()); refresh.ItemStringFormat = "{0} Hz"; mode.Children.Add(Ui.Row(Ui.Text(L.T("sf48164ea8a")), refresh)); mode.Children.Add(Ui.Line()); mode.Children.Add(Ui.Row(Ui.Text(L.T("s60cc99cc00")), orientation)); mode.Children.Add(Ui.Line());
        var apply = Ui.Button(L.T("s8a3a58dbb8"), () => _ = RunAsync(async () =>
        {
            var selected = display.Modes.First(m => m.Resolution == (string?)resolution.SelectedItem && m.Frequency == (uint)refresh.SelectedItem);
            await ChangeModeAsync(display, selected, (uint)orientation.SelectedIndex);
        }), true);
        void UpdateApply() => apply.IsEnabled = refresh.SelectedItem is uint hz && orientation.SelectedIndex >= 0 &&
            ((string?)resolution.SelectedItem != display.Current.Resolution || hz != display.Current.Frequency || (uint)orientation.SelectedIndex != display.Orientation);
        resolution.SelectionChanged += (_, _) => UpdateApply();
        refresh.SelectionChanged += (_, _) => UpdateApply();
        orientation.SelectionChanged += (_, _) => UpdateApply();
        UpdateApply();
        apply.HorizontalAlignment = HorizontalAlignment.Right;
        mode.Children.Add(Ui.Hint(apply, L.T("sb965922475"))); PageContent.Children.Add(Ui.Card(mode));
        if (CreateRefreshTimingControls(display) is UIElement timing) PageContent.Children.Add(Ui.Card(timing));
        var extras = new StackPanel(); extras.Children.Add(Ui.Setting(L.T("s850875a753"), Help(L.T("sce70a24e69")), Ui.Button(L.T("s86227fc6df"), () => OpenPip(display)))); extras.Children.Add(Ui.Line());
        extras.Children.Add(CreateColorControls(display)); extras.Children.Add(Ui.Line());
        if (CreateScanModeControls(display) is { } scanModes) { extras.Children.Add(scanModes); extras.Children.Add(Ui.Line()); }
        extras.Children.Add(Ui.Setting(L.T("sed1989d773"), Help(L.T("sb3bd567058")), Ui.Button(L.T("sab88522dca"), () => ExportEdid(display)))); PageContent.Children.Add(Ui.Card(extras));
        PageContent.Children.Add(Ui.Card(new Expander { Header = L.T("s6c07bb6fd4"), Content = Ui.Text(L.F("sed21095eec", display.Adapter, display.Device, display.X, display.Y, display.Id), 12) }));
    }
    internal UIElement CreateBrightnessSlider(DisplayInfo display, Func<DisplayInfo, int, Task> changed, bool compact = false)
    {
        string? help = compact ? null : Help(L.T(display.Brightness.HasValue ? "sa712089fbd" : "s664cd77896"));
        return CreateSlider(L.T("s8e2f643ad1"), GetCombinedBrightness(display), 100, value => changed(display, value), help, compact, display);
    }

    internal UIElement CreateQuickVolumeSlider(DisplayInfo display, Func<int, Task> changed) =>
        CreateSlider(L.T("s716ddda53d"), display.Volume, 100, changed, compact: true, volumeSteps: true);

    internal UIElement CreateSlider(string label, int? value, int maximum, Func<int, Task> changed, string? help = null, bool compact = false, DisplayInfo? brightnessDisplay = null, bool volumeSteps = false)
    {
        var stack = new StackPanel(); var amount = Ui.Text(value.HasValue ? value + "%" : L.T("s7c53786065"), 12, "#626B78"); stack.Children.Add(Ui.Row(Ui.Hint(Ui.Text(label), help), amount));
        var slider = new Slider { Value = value ?? 0, Maximum = maximum, IsEnabled = value.HasValue, Margin = new Thickness(0, compact ? 1 : 8, 0, 0), Height = compact ? 22 : 28 }; System.Windows.Automation.AutomationProperties.SetName(slider, label);
        Ui.Hint(slider, help);
        int confirmed = value ?? 0; bool updating = false, failed = false, dragging = false; IDisposable? keepOpen = null;
        void HoldPopup() => keepOpen ??= (Window.GetWindow(slider) as QuickWindow)?.KeepOpen();
        void ReleasePopup() { keepOpen?.Dispose(); keepOpen = null; }
        void RefreshPopup()
        {
            if (!failed && slider.IsLoaded && !dragging && !slider.IsMouseCaptureWithin && !stack.IsKeyboardFocusWithin)
                (Window.GetWindow(slider) as QuickWindow)?.Render();
        }
        var writer = new LatestValueWriter(changed, (requested, error) =>
        {
            failed = error != null;
            if (brightnessDisplay != null)
                confirmed = GetCombinedBrightness(brightnessDisplay);
            else if (error == null) confirmed = requested;
            if (error != null) Status(error.Message, true);
            // A slow write must not move the thumb away from a newer request.
            if ((int)Math.Round(slider.Value) == requested)
            {
                if (!dragging || error != null)
                {
                    updating = true; slider.Value = confirmed; updating = false;
                }
                amount.Text = (error == null ? "" : L.T("s28ee45284a")) + confirmed + "%";
                amount.ToolTip = error?.Message;
            }
        }, idle: () => { if (!dragging) ReleasePopup(); RefreshPopup(); });
        slider.ValueChanged += (_, _) =>
        {
            amount.Text = Math.Round(slider.Value) + "%";
            if (!updating) { HoldPopup(); writer.Request((int)Math.Round(slider.Value)); }
        };
        void OnBrightnessChanged(DisplayInfo current)
        {
            if (current.Id != brightnessDisplay?.Id || writer.IsActive || dragging) return;
            confirmed = GetCombinedBrightness(current);
            updating = true; slider.Value = confirmed; updating = false;
            amount.Text = confirmed + "%"; amount.ToolTip = null; failed = false;
        }
        if (brightnessDisplay != null) slider.Loaded += (_, _) => BrightnessChanged += OnBrightnessChanged;
        slider.Unloaded += (_, _) => { BrightnessChanged -= OnBrightnessChanged; dragging = false; writer.Stop(); ReleasePopup(); };
        slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent, new System.Windows.Controls.Primitives.DragStartedEventHandler((_, _) =>
        {
            dragging = true; HoldPopup();
        }));
        slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler(async (_, _) =>
        {
            dragging = false;
            await writer.WhenIdle;
            if (!dragging && !writer.IsActive)
            {
                updating = true; slider.Value = confirmed; updating = false;
                ReleasePopup();
            }
            RefreshPopup();
        }));
        stack.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshPopup));
        if (compact && brightnessDisplay != null) slider.LargeChange = 5;
        if (compact && volumeSteps && value.HasValue)
        {
            int StepValue(int delta) => Math.Clamp((int)Math.Round(slider.Value) + delta, 0, maximum);
            Button StepButton(string text, string key, int delta, string id)
            {
                var button = Ui.Button(text, () =>
                {
                    int next = StepValue(delta);
                    // Use the thumb's pending value so rapid clicks accumulate in
                    // the same serialized queue as dragging, even on slow DDC displays.
                    if (delta < 0 ? next < slider.Value : next > slider.Value) slider.Value = next;
                });
                button.Padding = new Thickness(6, 2, 6, 2); button.MinWidth = 36;
                button.ToolTip = L.T(key);
                System.Windows.Automation.AutomationProperties.SetName(button, L.T(key));
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, id);
                void UpdateEnabled() => button.IsEnabled = delta < 0 ? StepValue(delta) < slider.Value : StepValue(delta) > slider.Value;
                slider.ValueChanged += (_, _) => UpdateEnabled();
                UpdateEnabled();
                return button;
            }
            var controls = new DockPanel();
            var down = StepButton("−5", "quick.volumeDown", -5, "quick-volume-down");
            var up = StepButton("+5", "quick.volumeUp", 5, "quick-volume-up");
            DockPanel.SetDock(down, Dock.Left); controls.Children.Add(down);
            DockPanel.SetDock(up, Dock.Right); controls.Children.Add(up);
            slider.Margin = new Thickness(8, 1, 8, 0); controls.Children.Add(slider);
            stack.Children.Add(controls);
        }
        else stack.Children.Add(slider);
        return stack;
    }
    private void SetDimming(DisplayInfo display, double value)
    {
        ApplyDimmingOverlay(display, EffectiveOverlayDimming(value));
        display.Dimming = value;
    }
    private double EffectiveOverlayDimming(double baseline) =>
        idleDimming.EffectiveDimming(nightDimming.EffectiveDimming(baseline));

    private void ApplyDimmingOverlay(DisplayInfo display, double value)
    {
        if (value > 0) DisplayService.ValidateIdentity(display.Device, display.Id);
        RenderDimming(display, value);
    }
    private void RenderDimming(DisplayInfo display, double value)
    {
        if (value == 0) { if (dimWindows.Remove(display.Id, out var existing)) existing.Close(); }
        else { if (!dimWindows.TryGetValue(display.Id, out var overlay)) { overlay = new DimWindow(); dimWindows[display.Id] = overlay; overlay.Show(); } overlay.Position(display); overlay.Opacity = value / 100.0; }
    }
    internal async Task ChangeModeAsync(DisplayInfo display, DisplayMode mode, uint orientation, Window? dialogOwner = null, bool exactOutput = false)
    {
        if (modePending) throw new InvalidOperationException(L.T("s70da07b61e"));
        if (mode == display.Current && orientation == display.Orientation) { Status(L.T("sd75d70fe93")); return; }
        var original = DisplayService.CurrentMode(display.Device); var target = DisplayService.FindMode(display, mode, orientation, exactOutput);
        DisplayService.ApplyMode(display.Device, target, true, display.Id);
        var before = sceneBusy ? null : SceneService.CaptureOutput(Displays);
        var saved = before?.Displays.FirstOrDefault(d => string.Equals(d.Id, display.Id, StringComparison.OrdinalIgnoreCase));
        if (saved != null)
        {
            saved.Mode = new(original.Width, original.Height, original.Frequency);
            saved.Orientation = original.Orientation;
        }
        string recovery = ModeRecovery.Prepare(display.Device, display.Id, original); modePending = true; ModeLease? lease = null;
        try
        {
            lease = new ModeLease(() => DisplayService.ApplyMode(display.Device, target, expectedId: display.Id), () => DisplayService.ApplyMode(display.Device, original, expectedId: display.Id), () => File.Delete(recovery));
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
            if (!matches) throw new InvalidOperationException(L.T("sd26c587007"));
            lease.Keep(); Status(L.T("s3e899a02c0"));
            if (before != null) UndoHistory.Remember(before, outputOnly: true);
        }
        finally
        {
            if (lease?.Pending == true) { try { lease.Revert(); } catch (Exception ex) { Storage.Log("Rollback: " + ex); } }
            modePending = false; await RefreshAsync(sceneRefresh: sceneBusy);
        }
    }
    private void RenderGroups() => RenderDisplayGroups();
    private CheckBox Toggle(bool value, Action<bool> changed) { var toggle = new CheckBox { IsChecked = value }; toggle.Click += (_, _) => changed(toggle.IsChecked == true); return toggle; }
    internal void SavePreferences() => Save();
    private void Save() { try { Storage.Save(Preferences); } catch (Exception ex) { Status(L.T("s888403f2f3") + ex.Message, true); } }
    private void RenderApplication()
    {
        Heading(L.T("s63c73c4730"), L.T("se673548234"));
        var languages = new[] { "auto", "zh-Hans", "zh-Hant", "en", "ja", "ko" };
        var language = new ComboBox { ItemsSource = languages.Select(code => code == "auto" ? L.T("sf70df2348c") : L.NativeName(code)).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(languages, Preferences.Language)), MinWidth = 220 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(language, "application-language");
        language.SelectionChanged += (_, _) => { if (language.SelectedIndex < 0) return; Preferences.Language = languages[language.SelectedIndex]; Save(); Status(L.T("sb1a5e85793")); };
        var appearance = new StackPanel();
        appearance.Children.Add(Ui.Setting(L.T("s3d13868593"), L.T("sc2c1c7da26"), language));
        appearance.Children.Add(Ui.Line()); appearance.Children.Add(CreateThemeOption());
        PageContent.Children.Add(Ui.Card(appearance));
        var content = new StackPanel(); content.Children.Add(Ui.Setting(L.T("s78d94f90d2"), Help(L.T("s95e8d6a9f6")), Toggle(Preferences.CloseToTray, v => { Preferences.CloseToTray = v; Save(); })));
        content.Children.Add(Ui.Line()); content.Children.Add(CreateStartupOption());
        content.Children.Add(Ui.Line()); content.Children.Add(CreateAutoUpdateOption());
        content.Children.Add(Ui.Line()); content.Children.Add(Ui.Setting(L.T("s6b753d5905"), L.T("sab00c1067a"), Toggle(Preferences.ShowHelp, v => { Preferences.ShowHelp = v; Save(); Render(); }))); PageContent.Children.Add(Ui.Card(content));
        content.Children.Add(Ui.Line()); content.Children.Add(Ui.Setting(L.T("automation.restore"), Help(L.T("automation.restoreHelp")), Toggle(Preferences.RestoreBrightness, value => { Preferences.RestoreBrightness = value; Save(); })));
        RenderDimmingAndSceneOptions();
        PageContent.Children.Add(Ui.Card(Ui.Setting(L.T("s9167ee8b67"), Help(L.T("s22b00c690a")), Ui.Button(L.T("s2cbe04030d"), ExportDiagnostics))));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(L.T("s320388530e"), L.T("sde4f02c8c4")), Ui.Button(L.T("se90c12bb08"), ResetDimming))));
    }
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string StartupCommand => $"\"{Environment.ProcessPath}\" --tray";
    private UIElement CreateStartupOption()
    {
        string title = L.T("sde38b92ad2");
        string? hint = Help(L.T("sea9af1c6cc"));
        bool enabled = false;
        bool readable = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
            enabled = string.Equals(key?.GetValue(AppBrand.Name) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            readable = false;
            hint = ex.Message;
        }
        var toggle = Toggle(enabled, SetStartup);
        toggle.IsEnabled = readable;
        System.Windows.Automation.AutomationProperties.SetName(toggle, title);
        return Ui.Setting(title, hint, toggle);
    }
    private void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
            if (enabled) key.SetValue(AppBrand.Name, StartupCommand);
            else key.DeleteValue(AppBrand.Name, false);
            Status(enabled ? L.T("sd68c305919") : L.T("s01f73d5d98"));
        }
        catch (Exception ex) { Status(ex.Message, true); Render(); }
    }
    private void RenderMenu() => RenderPanelPreferences();
    private void RenderKeyboard() => RenderShortcuts();
    private void RenderPresets() => RenderScenes();
    private void SetupTray()
    {
        lightTrayGlyph = TrayIcon.UseLightGlyph();
        tray = new Forms.NotifyIcon { Text = L.T("s4f97d4b368"), Icon = TrayIcon.Create(), Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add(L.T("s770544f702"), null, (_, _) => Dispatcher.Invoke(ShowQuick)); menu.Items.Add(L.T("s3e86e94e26"), null, (_, _) => Dispatcher.Invoke(ShowSettings)); menu.Items.Add(L.T("sa13550662f"), null, (_, _) => Dispatcher.Invoke(() => _ = RefreshAsync())); menu.Items.Add(L.T("s8e6dbec08e"), null, (_, _) => Dispatcher.Invoke(ResetDimming));
        AddSceneCycleTrayItems(menu);
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(L.T("s2297788756"), null, (_, _) => Dispatcher.Invoke(Quit)); tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowQuick); }; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
    }
    internal void ShowSettings()
    {
        quick?.Close();
        bool wasVisible = IsVisible;
        Show();
        if (!wasVisible) Render();
        WindowState = WindowState.Normal; Activate();
    }
    internal void ShowDisplay(DisplayInfo display) { Navigate("displays", display.Id); ShowSettings(); }
    internal void ShowQuick()
    {
        if (sceneBusy) return;
        if (quick == null) { quick = new QuickWindow(this); quick.Closed += (_, _) => quick = null; }
        quick.Render(); quick.Show(); quick.PositionNearTray(); quick.Activate();
    }
    internal void OpenPip(DisplayInfo display) { var pip = new PipWindow(display); pipWindows.Add(pip); pip.Closed += (_, _) => pipWindows.Remove(pip); pip.Show(); }
    private void ResetDimming() => _ = RunAsync(ResetDimmingAsync);
    private IntPtr WindowProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg is 0x1a or 0x31a) Dispatcher.BeginInvoke(() =>
        {
            ThemeService.Apply(Preferences.Theme);
            bool light = TrayIcon.UseLightGlyph();
            if (tray == null || light == lightTrayGlyph) return;
            var replacement = TrayIcon.Create(light);
            var previous = tray.Icon; tray.Icon = replacement; lightTrayGlyph = light; previous?.Dispose();
        });
        if (msg == 0x7e || msg == 0x219 || msg == 0x2e0 || msg == 0x1a || (msg == 0x218 && (w.ToInt32() == 7 || w.ToInt32() == 18))) {
            ResetIdleDimming();
            if (msg == 0x218) resumeBrightness = true;
            hotplugTimer.Stop(); hotplugTimer.Start();
        }
        if (msg == 0x312)
        {
            handled = true;
            if (!sceneBusy && !modePending) HandleShortcut(w.ToInt32());
        }
        return IntPtr.Zero;
    }
    internal static void OpenSystem(string uri) { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(ex.Message, L.T("s0248b32130")); } }
    private void ExportEdid(DisplayInfo d)
    {
        var bytes = DisplayService.ReadEdid(d); if (bytes == null) { Status(L.T("s0a7c8dbc13"), true); return; }
        var dialog = new SaveFileDialog { FileName = "display-edid.bin", Filter = L.T("s5c68ae2ca4") }; if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, bytes); Status(L.T("sb9d3af8f83")); } catch (Exception ex) { Status(ex.Message, true); }
    }
    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog { FileName = "lumadesk-diagnostics.json", Filter = "JSON|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(Displays, Storage.Json)); Status(L.T("sc23961bae6")); } catch (Exception ex) { Status(ex.Message, true); }
    }
    private void ShutdownResources() { brightnessTransitionLifetime.Cancel(); hotplugTimer.Stop(); StopAutomation(); commands?.Dispose(); UnregisterShortcuts(); brightnessOsd?.Close(); tray?.Dispose(); foreach (var window in dimWindows.Values) window.Close(); dimWindows.Clear(); foreach (var window in pipWindows.ToArray()) window.Close(); quick?.Close(); Save(); System.Windows.Application.Current.Shutdown(); }
    internal void Quit() { if (modePending || sceneBusy) return; exiting = true; Close(); }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OpenQuick(object sender, RoutedEventArgs e) => ShowQuick();
}
