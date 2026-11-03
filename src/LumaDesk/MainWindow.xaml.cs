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
    private readonly List<PipWindow> pipWindows = new();
    private readonly DispatcherTimer hotplugTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Forms.NotifyIcon? tray;
    private bool lightTrayGlyph;
    private QuickWindow? quick;
    private string page = "displays";
    private string? selectedId;
    private bool scanning, exiting, modePending;
    private IntPtr hwnd;
    internal DisplayInfo? Selected => Displays.FirstOrDefault(d => d.Id == selectedId) ?? Displays.FirstOrDefault();

    public MainWindow()
    {
        InitializeComponent();
        Ui.ConfigureWindow(this);
        HelpSwitch.IsChecked = Preferences.ShowHelp;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(FitToMonitor);
        AddNavigation("displays", L.T("s1a13ea5910"), "\uE7F4"); AddNavigation("groups", L.T("s563a1990f3"), "\uE8A9"); AddNavigation("application", L.T("s63c73c4730"), "\uE713"); AddNavigation("quick", L.T("s770544f702"), "\uE8A5"); AddNavigation("keyboard", L.T("s864200bbb2"), "\uE765"); AddNavigation("presets", L.T("sf1de1e2621"), "\uE734"); AddNavigation("about", L.T("s52d25a9e30"), "\uE946");
        LanguageLabel.Text = L.NativeName(L.Language);
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
    private void AddNavigation(string key, string name, string icon)
    {
        var button = Ui.Button("", () => { page = key; selectedId = null; Render(); });
        button.Style = (Style)FindResource("Nav"); button.Content = Ui.IconLabel(icon, name); button.Tag = name; navButtons[key] = button; Navigation.Children.Add(button);
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
        try { await action(); } catch (Exception ex) { Status(ex.Message, true); MessageBox.Show(this, ex.Message, L.T("s79be13bdf0"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    internal async Task RefreshAsync()
    {
        if (scanning || modePending) return;
        scanning = true; Status(L.T("s117b82fa19"));
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
            Render(); quick?.Render(); Status(L.F("sd1362bfd3f", Displays.Count, DateTime.Now));
        }
        catch (Exception ex) { Status(L.T("sf7293a061e") + ex.Message, true); Render(); }
        finally { scanning = false; }
    }
    private void Render()
    {
        foreach (var pair in navButtons) pair.Value.Background = Ui.Brush(pair.Key == page ? "#E5E5E5" : "#00FFFFFF");
        Breadcrumb.Text = L.T("s424b6d3b48") + (navButtons.TryGetValue(page, out var navigation) ? (string)navigation.Tag : page);
        PageContent.Children.Clear(); DisplayTabs.Children.Clear();
        BuildTabs();
        switch (page)
        {
            case "displays": if (selectedId != null && Selected != null) RenderDisplay(Selected); else RenderOverview(); break;
            case "groups": RenderGroups(); break;
            case "application": RenderApplication(); break;
            case "quick": RenderMenu(); break;
            case "keyboard": RenderKeyboard(); break;
            case "presets": RenderPresets(); break;
            case "about": RenderAbout(); break;
        }
    }
    internal void RefreshView() { if (IsVisible) Render(); }
    private void BuildTabs()
    {
        AddTab(L.T("sfea405f9b0"), "\uE8FD", L.T("s129454c4b3"), selectedId == null, () => { page = "displays"; selectedId = null; Render(); });
        foreach (var display in Displays)
        {
            var d = display; AddTab(d.Name, "\uE7F4", d.Primary ? L.T("s93ff5822ca") : L.T("sc63fb70157"), selectedId == d.Id, () => { page = "displays"; selectedId = d.Id; Render(); });
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
        Heading(L.T("s20ccee3b80"), L.T("sf47552dbd8"));
        var head = Ui.Row(Ui.Text(L.T("sd6ff49aa98"), 13, "#626B7A", true), Ui.Button(L.T("s85db69cad5"), () => _ = RefreshAsync())); head.Margin = new Thickness(0, 0, 0, 12); PageContent.Children.Add(head);
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
                var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(Ui.Badge(d.Primary ? L.T("s93ff5822ca") : L.T("s5be0323e8a"), d.Primary ? "#007A8A" : "#38886F", d.Primary ? "#E6F2F4" : "#E6F2EC"));
                var open = Ui.Button("›", () => { selectedId = d.Id; Render(); }); open.FontSize = 22; open.Background = Brushes.Transparent; open.BorderThickness = new Thickness(0); open.Margin = new Thickness(10, 0, 0, 0); open.ToolTip = L.T("s84041df0ce") + d.Name; actions.Children.Add(open);
                list.Children.Add(Ui.Row(left, actions));
            }
            PageContent.Children.Add(Ui.Card(list));
            var layout = new StackPanel(); layout.Children.Add(Ui.Row(Ui.Text(L.T("sa6d9aa75a3"), 13, "#515B6A", true), Ui.Text(L.T("see4036c516"), 10, "#949BA8")));
            var canvas = new Canvas { Height = 132, Margin = new Thickness(0, 12, 0, 2), ClipToBounds = true };
            canvas.SizeChanged += (_, _) => DrawArrangement(canvas); layout.Children.Add(canvas);
            layout.Children.Add(Ui.Row(Ui.Text(L.T("s10e4ac7bce"), 11, "#8C94A0"), Ui.Button(L.T("s7c7d6a8555"), ShowLayout))); PageContent.Children.Add(Ui.Card(layout));
        }
        var virtualContent = new StackPanel(); virtualContent.Children.Add(Ui.Row(Ui.IconLabel("\uE7F8", L.T("s1b4f0e57f8")), Ui.Button(L.T("sf3b6083958"), ShowVirtualDisplays)));
        var desc = Ui.Text(L.T("s4d57c168c5"), 12, "#858C99"); desc.Margin = new Thickness(0, 12, 0, 0); virtualContent.Children.Add(desc); PageContent.Children.Add(Ui.Card(virtualContent));
        var bottom = Ui.Row(Ui.Labels(L.T("sc8847b16c8"), Help(L.T("s0bd80eb2d9"))), Ui.Button(L.T("saf91a997fb"), ShowQuick)); PageContent.Children.Add(Ui.Card(bottom));
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
            var screen = Ui.Button("", () => { selectedId = d.Id; Render(); }); screen.Content = inner; screen.Width = Math.Max(32, d.Current.Width * scale - 4); screen.Height = Math.Max(26, d.Current.Height * scale - 4); screen.Padding = new Thickness(4); screen.Background = new LinearGradientBrush(Ui.Brush("#D6E5FB").Color, Ui.Brush("#EDF0FA").Color, 65); screen.BorderBrush = Ui.Brush(d.Primary ? "#84ABEC" : "#BBC9DF"); screen.ToolTip = L.F("s6e3563d6e6", d.Name, d.Subtitle, d.X, d.Y);
            Canvas.SetLeft(screen, (canvas.ActualWidth - totalW * scale) / 2 + (d.X - minX) * scale); Canvas.SetTop(screen, 10 + (d.Y - minY) * scale); canvas.Children.Add(screen);
        }
    }
    private void RenderDisplay(DisplayInfo display)
    {
        Heading(display.Name, display.Subtitle + (display.Primary ? L.T("s28102bcda6") : ""));
        RenderDpiScaling(display);
        var light = new StackPanel(); light.Children.Add(Ui.Row(Ui.IconLabel("\uE706", L.T("s437926dd65")), Ui.Badge(display.HasDdc ? L.T("s77e64220a2") : display.HasWmi ? L.T("s16cc598b36") : L.T("sa81ff06ccd"))));
        light.Children.Add(Ui.Line());
        light.Children.Add(CreateSlider(L.T("sa92e3d7e03"), GetCombinedBrightness(display), 100, value => ChangeCombinedBrightness(display, value), Help(display.Brightness.HasValue ? L.T("sa712089fbd") : L.T("s664cd77896"))));
        light.Children.Add(Ui.Line());
        light.Children.Add(CreateSlider(L.T("s06dab760bb"), display.Brightness, 100, value => ChangeBrightness(display, value), Help(display.BrightnessMethod)));
        light.Children.Add(Ui.Line());
        light.Children.Add(CreateSlider(L.T("s417de11214"), display.Dimming, 85, value => { SetDimming(display, value); return Task.CompletedTask; }, Help(L.T("s723076bec1"))));
        if (display.Contrast.HasValue) { light.Children.Add(Ui.Line()); light.Children.Add(CreateSlider(L.T("s4cbc753240"), display.Contrast, 100, async value => { await Service.SetPercentAsync(display, 0x12, value); display.Contrast = value; Status(L.T("s1083ba917f")); })); }
        if (display.Volume.HasValue) { light.Children.Add(Ui.Line()); light.Children.Add(CreateSlider(L.T("s716ddda53d"), display.Volume, 100, async value => { await Service.SetPercentAsync(display, 0x62, value); display.Volume = value; Status(L.T("sd3fbc328d7")); })); }
        PageContent.Children.Add(Ui.Card(light));
        var mode = new StackPanel(); mode.Children.Add(Ui.IconLabel("\uE7F4", L.T("sdfc8b14cad"))); mode.Children.Add(Ui.Line());
        var resolutions = display.Modes.Select(m => m.Resolution).Distinct().ToList();
        var resolution = new ComboBox { ItemsSource = resolutions, SelectedItem = display.Current.Resolution, Width = 185 };
        var refresh = new ComboBox { Width = 185 }; var orientation = new ComboBox { ItemsSource = new[] { L.T("s35294d7f91"), L.T("se6121d2fdf"), L.T("sbc911c8ebe"), L.T("sf6f2dc11c9") }, SelectedIndex = (int)display.Orientation, Width = 185 };
        void UpdateRates() { var rates = display.Modes.Where(m => m.Resolution == (string?)resolution.SelectedItem).Select(m => m.Frequency).Distinct().ToList(); refresh.ItemsSource = rates; refresh.SelectedItem = rates.Contains(display.Current.Frequency) ? display.Current.Frequency : rates.FirstOrDefault(); }
        UpdateRates(); resolution.SelectionChanged += (_, _) => UpdateRates();
        mode.Children.Add(Ui.Row(Ui.Labels(L.T("s248a33b7b3"), Help(L.T("s2b8ac41596"))), resolution)); mode.Children.Add(Ui.Line()); mode.Children.Add(Ui.Row(Ui.Labels(L.T("sf48164ea8a"), "Hz"), refresh)); mode.Children.Add(Ui.Line()); mode.Children.Add(Ui.Row(Ui.Text(L.T("s60cc99cc00")), orientation)); mode.Children.Add(Ui.Line());
        mode.Children.Add(Ui.Row(Ui.Text(L.T("sb965922475"), 11, "#8B929E"), Ui.Button(L.T("s8a3a58dbb8"), () => _ = RunAsync(async () =>
        {
            var selected = display.Modes.First(m => m.Resolution == (string?)resolution.SelectedItem && m.Frequency == (uint)refresh.SelectedItem);
            await ChangeModeAsync(display, selected, (uint)orientation.SelectedIndex);
        }), true))); PageContent.Children.Add(Ui.Card(mode));
        var extras = new StackPanel(); extras.Children.Add(Ui.Row(Ui.Labels(L.T("s850875a753"), Help(L.T("sce70a24e69"))), Ui.Button(L.T("s86227fc6df"), () => OpenPip(display)))); extras.Children.Add(Ui.Line());
        extras.Children.Add(CreateColorControls(display)); extras.Children.Add(Ui.Line());
        extras.Children.Add(Ui.Row(Ui.Labels(L.T("sed1989d773"), Help(L.T("sb3bd567058"))), Ui.Button(L.T("sab88522dca"), () => ExportEdid(display)))); PageContent.Children.Add(Ui.Card(extras));
        PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s6c07bb6fd4"), L.F("sed21095eec", display.Adapter, display.Device, display.X, display.Y, display.Id))));
    }
    internal UIElement CreateSlider(string label, int? value, int maximum, Func<int, Task> changed, string? help = null, bool compact = false)
    {
        var stack = new StackPanel(); var amount = Ui.Text(value.HasValue ? value + "%" : L.T("s7c53786065"), 12, "#7C8594"); stack.Children.Add(Ui.Row(Ui.Labels(label, help), amount));
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
                amount.Text = L.T("s28ee45284a") + confirmed + "%"; amount.ToolTip = error.Message;
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
        if (failed.Count > 0) throw new InvalidOperationException(L.T("s9dda1f404b") + string.Join("、", failed) + L.T("s2707d07bb1"));
        Status(Preferences.SyncBrightness ? L.F("s350bc664da", targets.Count, value) : L.F("se52b079aa1", display.Name, value));
    }
    internal void SetDimming(DisplayInfo display, int value)
    {
        value = Math.Clamp(value, 0, 85);
        if (value > 0) DisplayService.ValidateIdentity(display.Device, display.Id);
        if (value == 0) { if (dimWindows.Remove(display.Id, out var existing)) existing.Close(); }
        else { if (!dimWindows.TryGetValue(display.Id, out var overlay)) { overlay = new DimWindow(); dimWindows[display.Id] = overlay; overlay.Show(); } overlay.Position(display); overlay.Opacity = value / 100.0; }
        display.Dimming = value; Status(L.F("s0d96fd5b34", display.Name, value));
    }
    internal async Task ChangeModeAsync(DisplayInfo display, DisplayMode mode, uint orientation, Window? dialogOwner = null)
    {
        if (modePending) throw new InvalidOperationException(L.T("s70da07b61e"));
        if (mode == display.Current && orientation == display.Orientation) { Status(L.T("sd75d70fe93")); return; }
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
            if (!matches) throw new InvalidOperationException(L.T("sd26c587007"));
            File.Delete(recovery); lease.Keep(); Status(L.T("s3e899a02c0"));
        }
        finally
        {
            if (lease?.Pending == true) { try { lease.Revert(); File.Delete(recovery); } catch (Exception ex) { Storage.Log("Rollback: " + ex); } }
            modePending = false; await RefreshAsync();
        }
    }
    private void RenderGroups()
    {
        Heading(L.T("s563a1990f3"), L.T("s745fafa566"));
        var content = new StackPanel(); content.Children.Add(Ui.Row(Ui.Labels(L.T("s48aa89296c"), Help(L.T("sd07a88c978"))), Toggle(Preferences.SyncBrightness, v => { Preferences.SyncBrightness = v; Save(); })));
        foreach (var d in Displays) { content.Children.Add(Ui.Line()); content.Children.Add(Ui.Row(Ui.Labels(d.Name, d.BrightnessMethod), Ui.Badge(d.Brightness.HasValue ? L.T("s514cc3b3d2") : L.T("s8fb81ba8f3"), d.Brightness.HasValue ? "#007A8A" : "#9298A2"))); }
        PageContent.Children.Add(Ui.Card(content)); PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s4365f944a0"), L.T("s7fee042e4f"))));
    }
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
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(L.T("s3d13868593"), L.T("sc2c1c7da26")), language)));
        var content = new StackPanel(); content.Children.Add(Ui.Row(Ui.Labels(L.T("s78d94f90d2"), Help(L.T("s95e8d6a9f6"))), Toggle(Preferences.CloseToTray, v => { Preferences.CloseToTray = v; Save(); })));
        content.Children.Add(Ui.Line()); content.Children.Add(Ui.Row(Ui.Labels(L.T("sde38b92ad2"), Help(L.T("sea9af1c6cc"))), Toggle(IsStartupEnabled(), SetStartup)));
        content.Children.Add(Ui.Line()); content.Children.Add(Ui.Row(Ui.Labels(L.T("s6b753d5905"), L.T("sab00c1067a")), Toggle(Preferences.ShowHelp, v => { Preferences.ShowHelp = v; HelpSwitch.IsChecked = v; Save(); Render(); }))); PageContent.Children.Add(Ui.Card(content));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(L.T("s9167ee8b67"), Help(L.T("s22b00c690a"))), Ui.Button(L.T("s2cbe04030d"), ExportDiagnostics))));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(L.T("s320388530e"), L.T("sde4f02c8c4")), Ui.Button(L.T("se90c12bb08"), ResetDimming))));
    }
    private static bool IsStartupEnabled() { using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"); return key?.GetValue("LumaDesk") != null; }
    private void SetStartup(bool enabled)
    {
        try { using var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"); if (enabled) key.SetValue("LumaDesk", "\"" + Environment.ProcessPath + "\" --tray"); else key.DeleteValue("LumaDesk", false); Status(enabled ? L.T("sd68c305919") : L.T("s01f73d5d98")); }
        catch (Exception ex) { Status(ex.Message, true); Render(); }
    }
    private void RenderMenu()
    {
        Heading(L.T("s770544f702"), L.T("s25f74e4b56"));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(L.T("saf91a997fb"), Help(L.T("s7b0dc62b8f"))), Ui.Button(L.T("s57fc4bf70b"), ShowQuick, true))));
        PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("sae6b362957"), L.T("s53d83ca77a"))));
    }
    private void RenderKeyboard()
    {
        Heading(L.T("s864200bbb2"), L.T("sd279db6ddf"));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(L.T("sc6a591a792"), Help(L.T("sb77c0d335e"))), Toggle(Preferences.Hotkeys, v => { Preferences.Hotkeys = v; Save(); ConfigureHotkeys(); }))));
        var shortcuts = new StackPanel(); shortcuts.Children.Add(Ui.Row(Ui.Text(L.T("s768e8a9c2a")), Ui.Badge("Ctrl + Alt + ↑"))); shortcuts.Children.Add(Ui.Line()); shortcuts.Children.Add(Ui.Row(Ui.Text(L.T("s970eb36fb3")), Ui.Badge("Ctrl + Alt + ↓"))); shortcuts.Children.Add(Ui.Line()); shortcuts.Children.Add(Ui.Row(Ui.Text(L.T("s8e6dbec08e")), Ui.Badge("Ctrl + Alt + 0"))); PageContent.Children.Add(Ui.Card(shortcuts));
    }
    private void RenderPresets()
    {
        Heading(L.T("s0b2fca3986"), L.T("s0ab652ed8d"));
        var name = new TextBox { Text = L.T("s3cbfca23b6"), MaxLength = 40, MinWidth = 200, Margin = new Thickness(0, 0, 12, 0) };
        PageContent.Children.Add(Ui.Card(Ui.Row(name, Ui.Button(L.T("sce211a5a47"), () =>
        {
            string label = name.Text.Trim(); if (label.Length == 0) { Status(L.T("s2c09021a16"), true); return; }
            if (Displays.Count == 0) { Status(L.T("se46fa4e5ef"), true); return; }
            if (Preferences.Presets.Any(p => p.Name.Equals(label, StringComparison.OrdinalIgnoreCase))) { Status(L.T("s1f09d8e896"), true); return; }
            Preferences.Presets.Add(new Preset { Name = label, Displays = Displays.Select(d => new PresetDisplay { Id = d.Id, Brightness = d.Brightness, Dimming = d.Dimming }).ToList() }); Save(); Render(); Status(L.T("sa0d6e1ed82"));
        }, true))));
        if (Preferences.Presets.Count == 0) PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s502f445e9e"), L.T("sd76248be1e"))));
        foreach (var preset in Preferences.Presets.ToList())
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(Ui.Button(L.T("s63c73c4730"), () => _ = RunAsync(() => ApplyPresetAsync(preset))));
            var remove = Ui.Button(L.T("s2f9daa8289"), () => { Preferences.Presets.Remove(preset); Save(); Render(); }); remove.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(remove);
            PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Labels(preset.Name, L.F("sb91b8e2562", preset.Displays.Count)), actions)));
        }
    }
    private async Task ApplyPresetAsync(Preset preset)
    {
        var failures = new List<string>(); int count = 0;
        foreach (var saved in preset.Displays)
        {
            var d = Displays.FirstOrDefault(d => d.Id == saved.Id); if (d == null) { failures.Add(L.T("se14b0dad1d")); continue; }
            try { if (saved.Brightness.HasValue && d.Brightness.HasValue) { await Service.SetPercentAsync(d, 0x10, saved.Brightness.Value); d.Brightness = saved.Brightness.Value; } SetDimming(d, saved.Dimming); count++; }
            catch (Exception ex) { failures.Add(d.Name); Storage.Log(ex.Message); }
        }
        quick?.Render(); Status(L.F("sa40a67f8f2", preset.Name, count) + (failures.Count > 0 ? L.T("s65c957e688") + string.Join("、", failures) : ""), failures.Count > 0);
    }
    private void RenderAbout()
    {
        Heading("LumaDesk for Windows", L.F("s50a0683450", AppBrand.Version));
        PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s64a3850bbb"), L.T("sf82e0234a5"))));
        PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s3aecd80a6e"), L.T("s910d13c018"))));
        PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s9dcc68b7b6"), L.T("sf72355f7b3"))));
        PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s038ad816c5"), L.T("sa9ad97fbe4"))));
    }
    private void SetupTray()
    {
        lightTrayGlyph = TrayIcon.UseLightGlyph();
        tray = new Forms.NotifyIcon { Text = L.T("s4f97d4b368"), Icon = TrayIcon.Create(), Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add(L.T("s770544f702"), null, (_, _) => Dispatcher.Invoke(ShowQuick)); menu.Items.Add(L.T("s3e86e94e26"), null, (_, _) => Dispatcher.Invoke(ShowSettings)); menu.Items.Add(L.T("sa13550662f"), null, (_, _) => Dispatcher.Invoke(() => _ = RefreshAsync())); menu.Items.Add(L.T("s8e6dbec08e"), null, (_, _) => Dispatcher.Invoke(ResetDimming)); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(L.T("s2297788756"), null, (_, _) => Dispatcher.Invoke(Quit)); tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowQuick); }; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
    }
    internal void ShowSettings() { quick?.Close(); Show(); WindowState = WindowState.Normal; Activate(); }
    internal void ShowDisplay(DisplayInfo display) { selectedId = display.Id; page = "displays"; Render(); ShowSettings(); }
    internal void ShowQuick()
    {
        if (quick == null) { quick = new QuickWindow(this); quick.Closed += (_, _) => quick = null; }
        quick.Render(); quick.Show(); quick.PositionNearTray(); quick.Activate();
    }
    internal void OpenPip(DisplayInfo display) { var pip = new PipWindow(display); pipWindows.Add(pip); pip.Closed += (_, _) => pipWindows.Remove(pip); pip.Show(); }
    private void ResetDimming() { foreach (var d in Displays) SetDimming(d, 0); Render(); quick?.Render(); Status(L.T("s4499bbc369")); }
    private void ConfigureHotkeys()
    {
        if (hwnd == IntPtr.Zero) return;
        for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(hwnd, i);
        if (!Preferences.Hotkeys) return;
        bool up = Native.RegisterHotKey(hwnd, 1, 0x4003, 0x26), down = Native.RegisterHotKey(hwnd, 2, 0x4003, 0x28), reset = Native.RegisterHotKey(hwnd, 3, 0x4003, 0x30);
        if (!up || !down || !reset) Status(L.T("sbf5ac2034f"), true);
    }
    private IntPtr WindowProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg is 0x1a or 0x31a) Dispatcher.BeginInvoke(() =>
        {
            bool light = TrayIcon.UseLightGlyph();
            if (tray == null || light == lightTrayGlyph) return;
            var replacement = TrayIcon.Create(light);
            var previous = tray.Icon; tray.Icon = replacement; lightTrayGlyph = light; previous?.Dispose();
        });
        if (msg == 0x7e || msg == 0x219 || msg == 0x2e0 || msg == 0x1a) { hotplugTimer.Stop(); hotplugTimer.Start(); }
        if (msg == 0x312)
        {
            handled = true;
            if (w.ToInt32() == 3) ResetDimming();
            else if (Selected is { } d) _ = RunAsync(async () => { await ChangeCombinedBrightness(d, Math.Clamp(GetCombinedBrightness(d) + (w.ToInt32() == 1 ? 5 : -5), 0, 100)); Render(); quick?.Render(); });
            else Status(L.T("s873e90682f"), true);
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
    private void ShutdownResources() { hotplugTimer.Stop(); for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(hwnd, i); tray?.Dispose(); foreach (var window in dimWindows.Values) window.Close(); dimWindows.Clear(); foreach (var window in pipWindows.ToArray()) window.Close(); quick?.Close(); Save(); System.Windows.Application.Current.Shutdown(); }
    internal void Quit() { if (modePending) return; exiting = true; Close(); }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OpenQuick(object sender, RoutedEventArgs e) => ShowQuick();
    private void HelpChanged(object sender, RoutedEventArgs e) { Preferences.ShowHelp = HelpSwitch.IsChecked == true; Save(); Render(); }
}
