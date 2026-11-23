// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static partial class Program
{
    private static int checks;
    private static string output = "";
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fixtures") return RunFixtures(args);
        output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/ui-tests"); Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", Path.Combine(output, "isolated-settings"));
        string language = args.Length > 1 ? args[1] : "zh-Hans";
        Storage.Save(new Preferences { Language = language, Theme = "light", Hotkeys = false, CloseToTray = false });
        var app = new App { IntegrationTest = true }; app.InitializeComponent();
        var window = new MainWindow();
        window.Loaded += async (_, _) =>
        {
            try
            {
                window.Hide(); // Match tray startup while the initial display scan completes.
                for (int i = 0; i < 150 && window.Displays.Count == 0; i++) await Task.Delay(100);
                await Task.Delay(200);
                Check(window.Displays.Count > 0, "Actual display scan populates overview");
                TestDeferredSettingsRendering(window);
                if (args.Contains("--demo"))
                {
                    await CaptureReadmeDemoAsync(window);
                    return;
                }
                if (language != "zh-Hans")
                {
                    await TestLocalizedUiAsync(window, language);
                    File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: {checks} {language} UI checks. No hardware settings were changed.\n");
                    Console.WriteLine($"{checks} {language} UI checks passed.");
                    return;
                }
                Check(window.Displays.All(d => d.Id.StartsWith("DISPLAY\\") && d.Modes.Count > 0), "Stable monitor instance IDs and real modes");
                bool rejected = false;
                try { DisplayService.ValidateIdentity(window.Displays[0].Device, "DISPLAY\\WRONG\\DEVICE"); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Stale device identity rejected before control command");
                Snapshot(window, "overview.png");
                var navigation = window.Navigation.Children.OfType<Button>().ToArray();
                string[] titles = { "显示器", "显示器组", "应用", "快捷面板", "键盘", "预设", "关于" };
                for (int i = 0; i < navigation.Length; i++)
                {
                    Click(navigation[i]); window.UpdateLayout();
                    Check(window.Breadcrumb.Text.EndsWith(titles[i]) && window.PageContent.Children.Count > 2, "Navigation: " + titles[i]);
                }
                Click(navigation[0]);
                foreach (var display in window.Displays)
                {
                    window.ShowDisplay(display); window.UpdateLayout();
                    var combos = Descendants<ComboBox>(window.PageContent).ToList();
                    Check(combos.Count == 3 && combos.All(c => c.SelectedIndex >= 0), "Resolution, refresh and rotation selected: " + display.Name);
                    var brightness = Descendants<Slider>(window.PageContent).Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == L.T("s8e2f643ad1"));
                    Check(brightness.IsEnabled && brightness.Value == window.GetCombinedBrightness(display) &&
                        !Descendants<Slider>(window.PageContent).Any(s => System.Windows.Automation.AutomationProperties.GetName(s) is "硬件亮度" or "额外调暗"),
                        "Settings shows one effective brightness control: " + display.Name);
                    if (display.DpiScale.CanSet)
                    {
                        Check(display.DpiScale.SupportedPercents.Contains(display.DpiScale.CurrentPercent), "Actual DPI belongs to available scales");
                        Check(display.PreferredWidth > 0 && display.PreferredHeight > 0, "Preferred output obtained from driver");
                        int candidate = display.DpiScale.SupportedPercents.First(p => p != display.DpiScale.CurrentPercent);
                        var choice = Descendants<Button>(window.PageContent).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == $"界面缩放 {candidate}%");
                        Click(choice); window.UpdateLayout();
                        Check(Descendants<TextBlock>(window.PageContent).Any(t => t.Text.Contains("看起来像约 " + DpiRules.LooksLike(display.Current, candidate))), "DPI choice previews logical desktop size");
                        var current = DisplayService.CurrentMode(display.Device);
                        Check(current.Width == display.Current.Width && current.Height == display.Current.Height && current.Frequency == display.Current.Frequency && DpiService.Read(display).CurrentPercent == display.DpiScale.CurrentPercent, "DPI preview changes neither output signal nor system scale");
                        window.ShowDisplay(display); window.UpdateLayout();
                        combos = Descendants<ComboBox>(window.PageContent).ToList();
                    }
                    foreach (string resolution in combos[0].Items.Cast<string>().Take(3))
                    {
                        combos[0].SelectedItem = resolution;
                        Check(combos[1].Items.Cast<uint>().All(hz => display.Modes.Any(m => m.Resolution == resolution && m.Frequency == hz)), "Refresh filtered by selected resolution");
                    }
                    window.ShowDisplay(display); Snapshot(window, "display-" + display.Number + ".png");
                    // CDS_TEST validates the exact current mode; this does not change hardware state.
                    var mode = DisplayService.FindMode(display, display.Current, display.Orientation);
                    DisplayService.ApplyMode(display.Device, mode, true);
                    Check(true, "Driver accepts current mode in test-only validation");
                }
                Click(navigation[5]); window.UpdateLayout();
                var save = Descendants<Button>(window.PageContent).First(b => b.Content as string == "保存当前设置"); Click(save);
                for (int i = 0; i < 150 && Storage.Load().Presets.Count == 0; i++) await Task.Delay(100);
                Check(Storage.Load().Presets.Count == 1, "Preset save persists in isolated test profile");
                Check(Storage.Load().Presets[0].Displays.All(d => d.Mode != null && d.X.HasValue && d.Primary.HasValue), "Saved scene includes output and placement for every screen");
                Check(Storage.Load().Presets.All(p => !p.OnConnect && p.AtTime == null && p.OnPower == null), "Automatic scene switching defaults off");
                var presetList = await window.ExecuteCommand(new ControlCommand("list-presets"));
                using (var listed = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(presetList)))
                    Check(presetList.Success && listed.RootElement.GetProperty("Data")[0].GetProperty("Id").GetString() == window.Preferences.Presets[0].Id,
                        "Running application serves saved preset IDs through the command handler");
                Snapshot(window, "scenes.png");
                foreach (var expander in Descendants<Expander>(window.PageContent).ToArray()) expander.IsExpanded = true;
                window.UpdateLayout();
                var delete = Descendants<Button>(window.PageContent).First(b => b.Content as string == "删除"); Click(delete);
                Check(Storage.Load().Presets.Count == 0, "Preset deletion updates persistence");
                var quick = new QuickWindow(window, dismissOnDeactivate: false); quick.Render(); quick.Show(); quick.UpdateLayout();
                Check(Descendants<Slider>(quick).Count() == 1 + (window.Displays.Single(d => d.Id == quick.SelectedDisplayId).DpiScale.CanSet ? 1 : 0), "Quick panel shows only the selected display's brightness and DPI sliders");
                Check(Descendants<Slider>(quick).Where(s => System.Windows.Automation.AutomationProperties.GetName(s) == "亮度").All(s => s.IsEnabled), "Combined quick brightness remains usable on software-only screens");
                Snapshot(quick, "quick-panel.png");
                var quickScroll = Descendants<ScrollViewer>(quick).First();
                Check(quick.Height < 620 && quickScroll.ScrollableHeight == 0, "Compact selected-screen panel sizes to content without blank height or scrolling");
                Snapshot(quick, "quick-panel.png"); quick.Close();
                TestKeepAwake(window);
                await TestQuickPlacementAsync(window);
                await TestFocusDisplayAsync(window);
                await TestQuickDisplayPickerAsync(window);
                TestNextScheduleFixture(window);
                await TestHdrBadgesAsync(window);
                await TestQuickDisplayListAsync(window);
                await TestQuickCompactLayoutAsync(window, offscreen: false);
                await TestMultiBrightnessAsync(window);
                await TestBrightnessStepsFixtureAsync(window);
                await TestVolumeStepsFixtureAsync(window);
                await TestInputSwitchUiAsync(window);
                await TestInputConnectionUiAsync(window);
                window.Hide(); await TestQuickScaleFixtureAsync(window); window.Show();
                await TestScanModeFixtureAsync(window);
                await TestRefreshTimingUiAsync(window);
                await TestQuickDpiAsync(window);
                await TestQuickModesAsync(window);
                if (window.Displays.Count > 1)
                {
                    var alternatePrimary = window.Displays.Last().Id;
                    LayoutService.Apply(window.Displays, window.Displays.Select(d => new ScreenPlacement(d.Id,d.X,d.Y,(int)d.Current.Width,(int)d.Current.Height,d.Id == alternatePrimary)), testOnly: true);
                    Check(window.Displays.All(d => { var m = DisplayService.CurrentMode(d.Device); return m.X == d.X && m.Y == d.Y; }), "Alternate primary layout passes whole-topology SDC_VALIDATE without moving monitors");
                }
                await TestLayoutAsync(window);
                await TestRelativePlacementFixtureAsync(window);
                TestAutoLayoutFixture(window);
                await TestLayoutSwapFixtureAsync(window);
                await TestLiveBrightnessAndDismissAsync(window);
                await TestBrightnessPopupLifetimeAsync(window);
                await TestBrightnessReadbackAsync(window);
                var pip = new PipWindow(window.Displays[0]); pip.Show(); await Task.Delay(700);
                Check(Descendants<System.Windows.Controls.Image>(pip).Single().Source != null, "PIP receives actual screen frames"); pip.Close();
                // Validate empty and unsupported-device states with fixtures; no native writes.
                var real = window.Displays; window.Displays = new List<DisplayInfo>(); Click(navigation[0]); window.UpdateLayout();
                Check(Descendants<TextBlock>(window.PageContent).Any(t => t.Text.Contains("尚未检测")), "Empty display state");
                window.Displays = real;
                var originalDpi = real[0].DpiScale;
                real[0].DpiScale = new DpiScaleInfo { UnavailableReason = "测试：自定义缩放不可直接调整" };
                window.ShowDisplay(real[0]); window.UpdateLayout();
                Check(Descendants<TextBlock>(window.PageContent).Any(t => t.Text.Contains("测试：自定义缩放不可直接调整")) && !Descendants<Button>(window.PageContent).Any(b => (b.Content as string)?.Contains("Windows") == true || b.Content as string == "应用缩放"), "Unsupported DPI explains limitation in app without external settings or fake apply");
                real[0].DpiScale = originalDpi;
                Click(navigation[0]); window.Width = 860; window.Height = 650; window.UpdateLayout(); Snapshot(window, "minimum-window.png");
                Check(window.ContentScroll.ViewportHeight > 200, "Minimum window retains scrollable controls");
                await TestWorkflowUiAsync(window);
                using (var icon = TrayIcon.Create()) using (var iconFile = File.Create(Path.Combine(output, "app.ico"))) icon.Save(iconFile);
                File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: {checks} UI integration checks. No hardware settings were changed.\n");
                Console.WriteLine($"{checks} UI checks passed.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL: " + ex); Environment.ExitCode = 1; }
            finally { window.Quit(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return Environment.ExitCode;
    }
    private static void Check(bool test, string name) { if (!test) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    private static async Task TestWorkflowUiAsync(MainWindow window)
    {
        var nav = window.Navigation.Children.OfType<Button>().ToArray();
        Click(nav[3]); window.UpdateLayout();
        Check(Descendants<TextBox>(window.PageContent).Count() == window.Displays.Count, "Panel preferences offer one simple name field per monitor");
        var display = window.Displays[0]; var options = window.Options(display);
        bool oldScaling = options.ShowScaling; options.ShowScaling = false;
        var quick = new QuickWindow(window, dismissOnDeactivate: false); quick.Render(); quick.Show(); quick.UpdateLayout();
        Check(!Descendants<Slider>(quick).Any(s => System.Windows.Automation.AutomationProperties.GetAutomationId(s) == "quick-dpi-slider-" + display.Number), "Scaling can be hidden without removing brightness");
        quick.Close(); options.ShowScaling = oldScaling;
        var other = new Window { Width = 100, Height = 100, ShowInTaskbar = false };
        quick = new QuickWindow(window); quick.Render(); quick.Show(); quick.Activate(); quick.UpdateLayout();
        Click(Descendants<Button>(quick).Single(b => b.Content as string == L.T("panel.pin")));
        other.Show(); other.Activate(); await Task.Delay(120);
        Check(quick.IsVisible, "Pinned quick panel remains open after losing focus");
        quick.Close(); other.Close();
        Click(nav[4]); window.UpdateLayout();
        Check(Descendants<TextBox>(window.PageContent).Count() == 3 && window.Preferences.HotkeyTarget == "pointer", "Shortcuts have editable bindings and an intuitive pointer target");
        Click(nav[0]); ThemeService.Apply("dark"); window.RefreshView(); window.UpdateLayout();
        Check(ThemeService.Brush("#FFFFFF").Color.R < 80 && ThemeService.Brush("#242424", true).Color.R > 180, "Dark theme has dark surfaces and legible text");
        Snapshot(window, "dark-overview.png");
        quick = new QuickWindow(window, dismissOnDeactivate: false); quick.Render(); quick.Show(); quick.UpdateLayout(); Snapshot(quick, "dark-quick.png"); quick.Close();
        Click(nav[2]); window.UpdateLayout(); Snapshot(window, "dark-application.png");
        var themeBox = Descendants<ComboBox>(window.PageContent).Last(); themeBox.IsDropDownOpen = true; await Task.Delay(80);
        Check(themeBox.IsDropDownOpen, "Themed combo opens and retains native selection behavior"); themeBox.IsDropDownOpen = false;
        ThemeService.Apply("light"); window.RefreshView();
        Check(ThemeService.Brush("#FFFFFF").Color.R == 255, "Theme returns to light without restarting");
        var pip = new PipWindow(display); pip.Show(); await Task.Delay(180); pip.Close(); await Task.Delay(350);
        Check(!pip.Capturing, "Closing PIP during capture drains the worker and releases buffers");
        var invalid = await window.ExecuteCommand(new ControlCommand("brightness", "missing-screen", 50));
        Check(!invalid.Success, "Unknown CLI display is rejected without hardware writes");
        var state = await window.ExecuteCommand(new ControlCommand("list"));
        Check(state.Success, "CLI status uses the live application's display state");
    }
    private static async Task TestLocalizedUiAsync(MainWindow window, string language)
    {
        string Text(string chinese) => L.T(L.Catalog("zh-Hans").First(p => p.Value == chinese).Key);
        Console.WriteLine("LANG selected=" + L.Language + " window=" + window.Language.IetfLanguageTag + " font=" + window.FontFamily.Source);
        Check(L.Language == language && window.Language.IetfLanguageTag.Equals(language, StringComparison.OrdinalIgnoreCase), "Window uses selected Windows UI language");
        Check(new[] { "window.minimize", "window.maximize", "window.close" }.All(key => Descendants<Button>(window).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == L.T(key))), "Window caption accessibility names are localized");
        var navigation = window.Navigation.Children.OfType<Button>().ToArray();
        string[] labels = { "显示器", "显示器组", "应用", "快捷面板", "键盘", "预设", "关于" };
        for (int i = 0; i < navigation.Length; i++)
        {
            Click(navigation[i]); window.UpdateLayout();
            Check(window.Breadcrumb.Text.EndsWith(Text(labels[i])) && window.PageContent.Children.Count > 2, "Localized navigation: " + labels[i]);
            Check(!Descendants<TextBlock>(window).Any(t => System.Text.RegularExpressions.Regex.IsMatch(t.Text, @"\bs[0-9a-f]{10}\b")), "No resource IDs leak into page " + i);
        }
        Click(navigation[2]); window.UpdateLayout();
        var selector = Descendants<ComboBox>(window).Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "application-language");
        Check(selector.Items.Count == 6 && selector.SelectedItem as string == L.NativeName(language), "Language picker offers system default and all five native names");
        selector.SelectedIndex = 0;
        Check(Storage.Load().Language == "auto" && L.Language == language, "Language preference persists without partially switching a live session");
        Snapshot(window, "application.png");
        var quick = new QuickWindow(window, dismissOnDeactivate: false);
        try
        {
            quick.Render(); quick.Show(); await Task.Delay(50); quick.UpdateLayout();
            Check(Descendants<Slider>(quick).Count() == 1 + (window.Displays.Single(d => d.Id == quick.SelectedDisplayId).DpiScale.CanSet ? 1 : 0), "Localized quick panel keeps selected display controls");
            Check(Descendants<TextBlock>(quick).Any(t => t.Text == Text("亮度")), "Quick brightness label translated");
            Snapshot(quick, "quick-panel.png");
            var more = Descendants<Button>(quick).First(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b).StartsWith("quick-more-toggle-"));
            Click(more); quick.UpdateLayout(); Snapshot(quick, "quick-more.png");
            Check(Descendants<Button>(quick).Any(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b).StartsWith("quick-color-toggle-")), "Localized advanced controls available");
            Click(Descendants<Button>(quick).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "quick-multi-toggle")); quick.UpdateLayout();
            Check(quick.IsBrightnessMultiSelect && Descendants<Slider>(quick).Count() == 1 && Descendants<TextBlock>(quick).Any(t => t.Text == L.F("quick.multiCount", 1)), "Localized multi-select exposes brightness only");
            Snapshot(quick, "quick-multi.png");
        }
        finally { quick.Close(); }
        using var dark = TrayIcon.Render(32, false); using var light = TrayIcon.Render(32, true);
        bool found = false, transparent = false;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            var a = dark.GetPixel(x,y); var b = light.GetPixel(x,y);
            CheckIconAlpha(a.A == b.A);
            if (a.A == 0) transparent = true;
            if (a.A > 240) { if (a.R > 40 || b.R < 245) throw new Exception("Incorrect theme glyph color"); found = true; }
        }
        Check(found && transparent, "Black and white tray icons retain identical transparent geometry");
        static void CheckIconAlpha(bool equal) { if (!equal) throw new Exception("Theme changed logo geometry"); }
        using var small = TrayIcon.Render(16, false);
        small.Save(Path.Combine(output, "tray-16.png"), System.Drawing.Imaging.ImageFormat.Png);
    }
    private static async Task TestLiveBrightnessAndDismissAsync(MainWindow window)
    {
        await TestLiveBrightnessFixtureAsync(window);
        var popup = new QuickWindow(window); popup.Render(); popup.Show(); popup.Activate(); await Task.Delay(40);
        var hold = popup.KeepOpen(); var outside = new Window { Width = 220, Height = 120, Title = "Dismiss test" }; outside.Show(); outside.Activate(); await Task.Delay(80);
        Check(popup.IsVisible, "Owned dialog lease protects quick popup from premature dismissal");
        hold.Dispose(); await Task.Delay(80);
        Check(!popup.IsVisible, "Quick popup closes after losing focus once guarded work completes");
        outside.Close(); popup.Close();
    }
    private static async Task TestBrightnessPopupLifetimeAsync(MainWindow window)
    {
        var real = window.Displays;
        var display = new DisplayInfo { Id = "brightness-lifetime", Name = "Brightness fixture", Number = 1, Brightness = 50 };
        window.Displays = new() { display }; window.Hide();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0; bool fail = false;
        var popup = new QuickWindow(window, applyBrightness: async (d, value) =>
        {
            writes++; await release.Task; if (fail) throw new InvalidOperationException("Brightness fixture failure");
            var level = BrightnessRules.Adjust(Math.Min(value, 70), d.Brightness, d.Dimming);
            d.Brightness = level.Hardware; d.Dimming = level.Dimming;
        });
        var outside = new Window { Width = 180, Height = 100, ShowInTaskbar = false };
        try
        {
            popup.Render(); popup.Show(); popup.Activate(); popup.UpdateLayout();
            var slider = Descendants<Slider>(popup).Single(); slider.Focus();
            slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            slider.Value = 65; await Task.Delay(100);
            popup.Render();
            Check(writes == 1 && popup.IsVisible && Descendants<Slider>(popup).Contains(slider), "Brightness write preserves the active popup and thumb");
            release.SetResult(); await Task.Delay(100);
            popup.Render();
            Check(popup.IsVisible && Descendants<Slider>(popup).Contains(slider), "Finishing a write mid-drag does not rebuild or dismiss the brightness slider");
            slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Task.Delay(80); popup.Render();
            Check(popup.IsVisible && Descendants<Slider>(popup).Contains(slider) && slider.IsKeyboardFocusWithin, "Release retains the focused brightness slider for continued adjustment");
            // Exercise the real nonactivating dimming window without darkening any pixels.
            var overlay = new DimWindow(); overlay.Show(); await Task.Delay(40); overlay.Close(); await Task.Delay(40);
            Check(popup.IsVisible && popup.IsActive, "Creating and removing an invisible dimming layer preserves popup activation");
            slider.Value = 85; await Task.Delay(100);
            Check(writes == 2 && popup.IsVisible && slider.Value == 70, "The focused thumb reads back a capped brightness without sending another write");
            fail = true; slider.Value = 75; await Task.Delay(100);
            Check(popup.IsVisible && slider.Value == 70, "Failed brightness write restores the last value without closing the panel");
            outside.Show(); outside.Activate(); await Task.Delay(100);
            Check(!popup.IsVisible, "Clicking another window still dismisses the brightness panel");
            popup = new QuickWindow(window, applyBrightness: (_, _) => Task.CompletedTask); popup.Render(); popup.Show(); popup.Activate();
            popup.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(popup), Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            await Task.Delay(40);
            Check(!popup.IsVisible, "Escape still closes the panel after the brightness lifetime fix");
        }
        finally { release.TrySetResult(); await Task.Delay(50); popup.Close(); outside.Close(); window.Displays = real; window.Show(); }
    }
    private static async Task TestBrightnessReadbackAsync(MainWindow window)
    {
        var real = window.Displays;
        bool sync = window.Preferences.SyncBrightness;
        var display = new DisplayInfo { Id = "brightness-readback", Name = "Readback", Number = 1, Dimming = 42.5 };
        window.Displays = new() { display }; window.Preferences.SyncBrightness = false;
        var host = new Window { Content = window.CreateBrightnessSlider(display, window.ChangeCombinedBrightness), Width = 250, Height = 120 };
        try
        {
            host.Show(); host.UpdateLayout();
            var slider = Descendants<Slider>(host).Single(); slider.Focus();
            Check(slider.Value == 50, "Software-only slider reads fractional overlay opacity");
            // The fixture has no native device. Returning to 100% removes its dimming without a hardware write.
            var reply = await window.ExecuteCommand(new ControlCommand("brightness", display.Id, 100));
            Check(reply.Success && display.Dimming == 0 && slider.Value == 100, "CLI and a focused slider agree after removing dimming");
            window.Options(display).Minimum = 100;
            slider.Value = 12; await Task.Delay(100);
            Check(slider.Value == 100 && window.Preferences.RememberedBrightness[display.Id].Dimming == 0,
                "A limited slider reports and remembers the applied software brightness");
        }
        finally
        {
            host.Close(); window.Displays = real; window.Preferences.SyncBrightness = sync;
            window.Preferences.DisplayOptions.Remove(display.Id); window.Preferences.RememberedBrightness.Remove(display.Id);
        }
    }

    private static async Task TestLayoutAsync(MainWindow window)
    {
        var real = window.Displays;
        var a = new DisplayInfo { Number = 1, Id = "layout-a", Name = "左屏", X = -1920, Y = 0, Current = new(1920,1080,60) };
        var b = new DisplayInfo { Number = 2, Id = "layout-b", Name = "右屏", Primary = true, Current = new(2560,1440,144) };
        window.Displays = new() { a, b }; window.Hide();
        int calls = 0; IReadOnlyList<ScreenPlacement>? received = null; bool fail = false;
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var layout = new LayoutWindow(window, async p => { calls++; received = p; await hold.Task; if (fail) throw new InvalidOperationException("测试：排列应用被拒绝"); });
        try
        {
            layout.Show(); layout.UpdateLayout(); Snapshot(layout, "layout.png");
            Check(Descendants<FrameworkElement>(layout).Count(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e).StartsWith("layout-screen-")) == 2, "Layout renders each monitor in its physical position");
            var undo = Descendants<Button>(layout).Single(btn => System.Windows.Automation.AutomationProperties.GetAutomationId(btn) == "layout-undo");
            var redo = Descendants<Button>(layout).Single(btn => System.Windows.Automation.AutomationProperties.GetAutomationId(btn) == "layout-redo");
            Check(!undo.IsEnabled && !redo.IsEnabled, "Loaded layout has no undo or redo steps");
            var primary = Descendants<ComboBox>(layout).Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "layout-primary"); primary.SelectedItem = a;
            Check(calls == 0 && layout.Placements.Single(p => p.Id == a.Id).Primary, "Primary selection only previews and sends no display command");
            Check(ApplicationCommands.Undo.InputGestures.OfType<KeyGesture>().Any(g => g.Key == Key.Z && g.Modifiers == ModifierKeys.Control) &&
                ApplicationCommands.Redo.InputGestures.OfType<KeyGesture>().Any(g => g.Key == Key.Y && g.Modifiers == ModifierKeys.Control), "Layout undo and redo commands expose Ctrl+Z and Ctrl+Y");
            Check(ApplicationCommands.Undo.CanExecute(null, layout) && undo.IsEnabled, "Primary preview enables layout undo command and button");
            ApplicationCommands.Undo.Execute(null, layout);
            Check(ReferenceEquals(primary.SelectedItem, b) && layout.Placements.Single(p => p.Id == b.Id).Primary && !undo.IsEnabled && redo.IsEnabled, "Undo command restores the preview and primary selector");
            ApplicationCommands.Redo.Execute(null, layout);
            Check(ReferenceEquals(primary.SelectedItem, a) && undo.IsEnabled && !redo.IsEnabled, "Redo command restores the primary preview without a recursive edit");
            Click(undo); Click(redo);
            Check(ReferenceEquals(primary.SelectedItem, a) && calls == 0, "Undo and redo buttons only change the preview");
            var beforeCoordinates = layout.Placements.ToArray();
            var xBox = Descendants<TextBox>(layout).First(); xBox.Text = "-1800";
            Click(Descendants<Button>(layout).Single(btn => btn.Content as string == L.T("s4a05c9ce76")));
            Check(layout.Placements[0].X == -1800, "Coordinate update records a preview edit");
            Click(undo);
            Check(layout.Placements.SequenceEqual(beforeCoordinates) && xBox.Text == "-1920", "Undo coordinates updates the preview and its text fields");
            Click(Descendants<Button>(layout).Single(btn => btn.Content as string == L.T("s86d5bec31e")));
            Check(layout.Placements[1].Y == 1080 && !redo.IsEnabled, "New arrangement replaces the redo path");
            Click(undo);
            Check(layout.Placements.SequenceEqual(beforeCoordinates), "Undo restores all arranged monitors in one step");
            xBox.Focus(); xBox.SelectAll(); xBox.SelectedText = "-1200";
            ApplicationCommands.Undo.Execute(null, xBox);
            Check(xBox.Text != "-1200" && layout.Placements.SequenceEqual(beforeCoordinates), "Undo inside a coordinate field edits text without undoing the layout");
            // Toggling undo clears WPF's text history before checking command routing.
            xBox.IsUndoEnabled = false; xBox.IsUndoEnabled = true;
            ApplicationCommands.Undo.Execute(null, xBox);
            Check(layout.Placements.SequenceEqual(beforeCoordinates), "An empty text undo history does not fall through to layout undo");
            var apply = Descendants<Button>(layout).Single(btn => System.Windows.Automation.AutomationProperties.GetAutomationId(btn) == "layout-apply");
            Click(apply); await Task.Delay(20);
            Check(calls == 1 && received!.Single(p => p.Id == a.Id).X == 0 && received!.Single(p => p.Id == b.Id).X == 1920 && !window.IsVisible, "Layout applies a normalized exact set from its own window without opening settings");
            Click(apply); Check(calls == 1 && !apply.IsEnabled, "Pending layout blocks duplicate submission");
            Check(!undo.IsEnabled && !redo.IsEnabled && !ApplicationCommands.Undo.CanExecute(null, layout) && !ApplicationCommands.Redo.CanExecute(null, layout), "Pending apply blocks preview undo and redo");
            hold.SetResult(); await Task.Delay(60);
            Check(!undo.IsEnabled && !redo.IsEnabled && !ApplicationCommands.Undo.CanExecute(null, layout), "Successful apply reloads the layout and clears preview history");
            primary.SelectedItem = a; fail = true; Click(apply); await Task.Delay(60);
            Check(calls == 2 && Descendants<TextBlock>(layout).Any(t => t.Text == "测试：排列应用被拒绝"), "Layout errors remain visible in app and controls recover");
            Check(undo.IsEnabled, "Failed apply keeps preview history available");
            Click(undo);
            Check(ReferenceEquals(primary.SelectedItem, b) && calls == 2, "Undo after a rejected apply restores the preview without another display write");
        }
        finally { hold.TrySetResult(); await Task.Delay(20); layout.Close(); window.Displays = real; window.Show(); }
    }
    private static async Task TestQuickPlacementAsync(MainWindow window)
    {
        var work = new Native.Rect { Left = -2560, Top = -200, Right = 0, Bottom = 1200 };
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0, 3.0, 1.0 })
        {
            var rect = PopupPlacement.Calculate(work, 360, 482, scale, scale);
            Check(rect.Right == -(int)Math.Round(12 * scale) && rect.Bottom == 1200 - (int)Math.Round(12 * scale) &&
                rect.Left >= work.Left && rect.Top >= work.Top && rect.Right - rect.Left == (int)Math.Round(360 * scale),
                $"Popup stays inside negative-origin work area and preserves edge gap at {scale * 100}%");
        }
        var tiny = PopupPlacement.Calculate(new Native.Rect { Right = 320, Bottom = 240 }, 360, 620, 3, 3);
        Check(tiny.Left == 36 && tiny.Top == 36 && tiny.Right == 284 && tiny.Bottom == 204, "Oversized popup fits small work area at high DPI");
        var quick = new QuickWindow(window, dismissOnDeactivate: false);
        try
        {
            quick.Render(); quick.Show(); await Task.Delay(80);
            var hwnd = new WindowInteropHelper(quick).Handle;
            var anchor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2), ref anchor);
            var monitors = new List<Native.MonitorInfo>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref Native.Rect rect, IntPtr data) =>
            {
                var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
                if (Native.GetMonitorInfo(monitor, ref info)) monitors.Add(info);
                return true;
            }, IntPtr.Zero);
            void CheckAnchor(string name)
            {
                Native.GetWindowRect(hwnd, out var rect);
                var dpi = VisualTreeHelper.GetDpi(quick);
                Check(Math.Abs(anchor.Work.Right - rect.Right - Math.Round(12 * dpi.DpiScaleX)) <= 1 &&
                    Math.Abs(anchor.Work.Bottom - rect.Bottom - Math.Round(12 * dpi.DpiScaleY)) <= 1 &&
                    rect.Left >= anchor.Work.Left && rect.Top >= anchor.Work.Top, name);
            }
            CheckAnchor("Opened popup is anchored using physical monitor coordinates");
            int dpiEvents = 0;
            quick.DpiChanged += (_, _) => dpiEvents++;
            foreach (var monitor in monitors.Concat(monitors))
            {
                // Exercise real WPF per-monitor DPI messages without changing any display settings.
                Native.SetWindowPos(hwnd, IntPtr.Zero, monitor.Work.Left + 40, monitor.Work.Top + 40, 0, 0, 0x0015);
                await Task.Delay(100);
                CheckAnchor("OS relocation/DPI transition returns popup to original monitor: " + monitor.Device);
            }
            if (window.Displays.Select(d => d.DpiScale.CurrentPercent).Distinct().Count() > 1)
                Check(dpiEvents > 0, "Placement regression exercises actual mixed-DPI window transitions");
            var toggle = Descendants<Button>(quick).First(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b).StartsWith("quick-more-toggle-"));
            Click(toggle); await Task.Delay(80); CheckAnchor("Expanded popup retains bottom-right anchor");
            toggle = Descendants<Button>(quick).First(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b).StartsWith("quick-more-toggle-"));
            Click(toggle); await Task.Delay(80); CheckAnchor("Collapsed popup retains bottom-right anchor");
            Native.GetWindowRect(hwnd, out var stable);
            await Task.Delay(200);
            Native.GetWindowRect(hwnd, out var after);
            Check(stable.Left == after.Left && stable.Top == after.Top && stable.Right == after.Right && stable.Bottom == after.Bottom,
                "Placement settles without a reposition/resize feedback loop");
        }
        finally { quick.Close(); window.Preferences.QuickMoreDisplayId = null; }
    }
    private static async Task TestQuickDpiAsync(MainWindow window)
    {
        var real = window.Displays;
        var first = new DisplayInfo { Number = 1, Id = "fixture-a", Name = "测试屏幕 A", Current = new(2560,1440,144), DpiScale = DpiRules.FromRelative(0,0,3) };
        var second = new DisplayInfo { Number = 2, Id = "fixture-b", Name = "测试屏幕 B", Current = new(3840,2160,60), DpiScale = DpiRules.FromRelative(0,1,2) };
        var unsupported = new DisplayInfo { Number = 3, Id = "fixture-c", Name = "测试屏幕 C", Current = new(1920,1080,60), DpiScale = new DpiScaleInfo { UnavailableReason = "测试：自定义缩放不可直接调整" } };
        window.Displays = new List<DisplayInfo> { first, second, unsupported }; window.Hide();
        int calls = 0, requestedPercent = 0; DisplayInfo? requestedDisplay = null; Window? requestedOwner = null; bool fail = false;
        TaskCompletionSource<bool>? hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var quick = new QuickWindow(window, async (display, percent, parent) =>
        {
            calls++; requestedPercent = percent; requestedDisplay = display; requestedOwner = parent;
            if (hold != null) await hold.Task;
            if (fail) throw new InvalidOperationException("测试：缩放命令被拒绝");
            display.DpiScale.CurrentPercent = percent;
        }, dismissOnDeactivate: false);
        Slider FindDpi(int number) => Descendants<Slider>(quick).Single(s => System.Windows.Automation.AutomationProperties.GetAutomationId(s) == "quick-dpi-slider-" + number);
        void BeginDrag(Slider slider) => slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        void EndDrag(Slider slider, bool canceled = false) => slider.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled) { RoutedEvent = Thumb.DragCompletedEvent });
        try
        {
            quick.Render(); quick.Show(); quick.UpdateLayout();
            Check(!window.IsVisible && quick.IsVisible, "Quick panel works while main settings remains hidden");
            Check(!Descendants<Button>(quick).Any(b => b.Content as string == "应用缩放"), "Quick DPI slider needs no Apply button");
            SelectQuick(quick, 3);
            Check(!Descendants<Slider>(quick).Any(s => System.Windows.Automation.AutomationProperties.GetAutomationId(s).StartsWith("quick-dpi-slider-")) && Descendants<TextBlock>(quick).Any(t => t.Text.Contains("测试：自定义缩放不可直接调整")) && !Descendants<Button>(quick).Any(b => (b.Content as string)?.Contains("Windows") == true), "Unsupported selected DPI remains in app without actionable slider or external settings");
            SelectQuick(quick, 2);
            var slider = FindDpi(2); BeginDrag(slider); slider.Value = 0; await Task.Delay(150);
            Check(calls == 1 && !window.IsVisible, "DPI is applied while dragging, before pointer release");
            var scaleLabels = Descendants<TextBlock>((StackPanel)slider.Parent).Select(t => t.Text).ToArray();
            Check(scaleLabels.Contains("缩放 150%") && !scaleLabels.Any(t => t.Contains("2560 × 1440")), "Quick DPI slider updates its percentage without a logical resolution caption");
            Check(calls == 1 && ReferenceEquals(requestedDisplay, second) && requestedPercent == 150 && ReferenceEquals(requestedOwner, quick), "Live change applies exact display and supported percentage in quick window context");
            quick.Render(); quick.UpdateLayout();
            Check(ReferenceEquals(slider, FindDpi(2)) && slider.IsEnabled, "Pending live DPI retains an enabled stable drag control");
            SelectQuick(quick, 1);
            Check(quick.SelectedDisplayId == second.Id && ReferenceEquals(slider, FindDpi(2)), "Pending DPI prevents target switches and retains the active screen");
            slider.Value = 2; slider.Value = 1; await Task.Delay(100);
            Check(calls == 1, "Slow DPI write queues only the latest subsequent target");
            var popupHandle = new WindowInteropHelper(quick).Handle;
            Native.GetWindowRect(popupHandle, out var pinned);
            quick.Left -= 64; quick.Top -= 48;
            await Task.Delay(80);
            Native.GetWindowRect(popupHandle, out var repinned);
            Check(pinned.Left == repinned.Left && pinned.Top == repinned.Top && ReferenceEquals(slider, FindDpi(2)),
                "Popup corrects OS drift during a pending drag without rebuilding the active slider");
            hold.SetResult(true); hold = null; await Task.Delay(180); quick.UpdateLayout();
            Check(calls == 2 && second.DpiScale.CurrentPercent == 125 && ReferenceEquals(slider, FindDpi(2)), "Latest queued target is applied without replacing the active thumb");
            EndDrag(slider); await Task.Delay(60);
            Check(FindDpi(2).IsEnabled && FindDpi(2).Value == 1 && !window.IsVisible, "Release keeps the confirmed live scale without opening settings");
            SelectQuick(quick, 1); slider = FindDpi(1); slider.Value = 2; await Task.Delay(10); slider.Value = 1; await Task.Delay(180);
            Check(calls == 3 && ReferenceEquals(requestedDisplay, first) && requestedPercent == 150, "Fast keyboard input coalesces to latest supported step");
            SelectQuick(quick, 2); quick.UpdateLayout(); fail = true; slider = FindDpi(2);
            Click(Descendants<Button>(quick).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "quick-dpi-decrease-2"));
            await Task.Delay(420); quick.UpdateLayout();
            Check(calls == 4 && Descendants<TextBlock>(quick).Any(t => t.Text == "测试：缩放命令被拒绝") && FindDpi(2).Value == 1, "Quick failure is displayed inline and value reloads actual current scale");
            quick.Render(); quick.UpdateLayout();
            Check(Descendants<TextBlock>(quick).Any(t => t.Text == "测试：缩放命令被拒绝") && !window.IsVisible, "Inline failure survives discovery rerender without opening main settings");
            SelectQuick(quick, 1); fail = false; slider = FindDpi(1); BeginDrag(slider); slider.Value = 0; await Task.Delay(160); EndDrag(slider, true); await Task.Delay(60);
            Check(calls == 5 && first.DpiScale.CurrentPercent == 175, "Cancel keeps an already confirmed live setting without a stale queued command");
            var decrease = Descendants<Button>(quick).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "quick-dpi-decrease-1");
            decrease.Focus(); Click(decrease); await Task.Delay(180); quick.Render();
            Check(calls == 6 && first.DpiScale.CurrentPercent == 150 && decrease.IsKeyboardFocused &&
                ReferenceEquals(decrease, Descendants<Button>(quick).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "quick-dpi-decrease-1")),
                "A keyboard nudge keeps its focused button through completion and discovery refresh");
            slider = FindDpi(1); slider.Value = 2; quick.Close(); await Task.Delay(180);
            Check(calls == 7 && !quick.IsVisible && first.DpiScale.CurrentPercent == 125, "Closing waits for final live target then closes without losing adjustment");
        }
        finally
        {
            hold?.TrySetResult(true); await Task.Delay(50); quick.Close(); window.Displays = real; window.Show();
        }
    }
    private static async Task TestQuickModesAsync(MainWindow window)
    {
        var real = window.Displays; var savedMore = window.Preferences.QuickMoreDisplayId;
        var current = new DisplayMode(2560, 1440, 144); var slower = new DisplayMode(2560, 1440, 60); var smaller = new DisplayMode(1920, 1080, 60);
        var first = new DisplayInfo { Number = 1, Id = "mode-fixture-a", Name = "测试屏幕 A", Current = current, Orientation = 1, Volume = 45, DpiScale = DpiRules.FromRelative(0, 0, 3), Modes = new() { current, slower, smaller, slower } };
        var second = new DisplayInfo { Number = 2, Id = "mode-fixture-b", Name = "测试屏幕 B", Current = new(3840, 2160, 60), DpiScale = DpiRules.FromRelative(0, 1, 2) };
        window.Displays = new() { first, second }; window.Preferences.QuickMoreDisplayId = null; window.Hide();
        int calls = 0, dpiCalls = 0; uint requestedOrientation = 0; DisplayInfo? requestedDisplay = null; DisplayMode? requestedMode = null; Window? requestedOwner = null; bool fail = false;
        TaskCompletionSource<bool>? hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var quick = new QuickWindow(window, (_, _, _) => { dpiCalls++; return Task.CompletedTask; }, async (display, mode, orientation, parent) =>
        {
            calls++; requestedDisplay = display; requestedMode = mode; requestedOrientation = orientation; requestedOwner = parent;
            if (hold != null) await hold.Task;
            if (fail) throw new InvalidOperationException("测试：输出模式被拒绝");
            display.Current = mode;
        }, dismissOnDeactivate: false);
        bool Has(string id) => Descendants<FrameworkElement>(quick).Any(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e) == id);
        void Toggle(string id) { Click(Descendants<Button>(quick).Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == id)); quick.UpdateLayout(); }
        ListBox List(string id) => Descendants<ListBox>(quick).Single(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e) == id);
        try
        {
            quick.Render(); quick.Show(); quick.UpdateLayout();
            Check(!Has("quick-output-toggle-1") && !Descendants<TextBlock>(quick).Any(t => t.Text == "显示器音量"), "Mode and volume controls stay inside More by default");
            Check(!Has("quick-volume-down") && !Has("quick-volume-up"), "Volume steps stay inside More by default");
            Toggle("quick-more-toggle-1");
            Check(Has("quick-output-toggle-1") && Has("quick-refresh-toggle-1") && Descendants<TextBlock>(quick).Any(t => t.Text == "显示器音量"), "More exposes real output, refresh and supported monitor volume controls");
            Check(Has("quick-volume-down") && Has("quick-volume-up"), "More exposes volume steps for the selected supported monitor");
            Toggle("quick-refresh-toggle-1"); var rates = List("quick-refresh-list-1");
            Check(rates.Items.Cast<DisplayMode>().SequenceEqual(new[] { current, slower }) && Equals(rates.SelectedItem, current), "Refresh list filters current pixel dimensions, deduplicates and selects current rate");
            Check(calls == 0, "Expanding mode lists does not send a display command");
            Toggle("quick-output-toggle-1"); var modes = List("quick-output-list-1");
            Check(!Has("quick-refresh-list-1") && modes.Items.Count == 3 && Equals(modes.SelectedItem, current), "Output list contains real modes and excludes simultaneous refresh subsection");
            Snapshot(quick, "quick-modes.png");
            modes.SelectedItem = smaller;
            Check(calls == 1 && ReferenceEquals(requestedDisplay, first) && Equals(requestedMode, smaller) && requestedOrientation == 1 && ReferenceEquals(requestedOwner, quick) && !window.IsVisible, "Selecting mode immediately targets exact display, mode, orientation and quick context");
            quick.Render(); quick.UpdateLayout();
            Check(ReferenceEquals(modes, List("quick-output-list-1")) && !modes.IsEnabled, "Pending mode change disables controls and defers discovery rerender");
            modes.SelectedItem = slower;
            SelectQuick(quick, 2);
            Check(quick.SelectedDisplayId == first.Id, "Pending output change prevents a display switch");
            var dpi = Descendants<Slider>(quick).Single(s => System.Windows.Automation.AutomationProperties.GetAutomationId(s) == "quick-dpi-slider-1"); dpi.Value = 0; await Task.Delay(400);
            Check(calls == 1 && dpiCalls == 0, "Mode transaction blocks overlapping mode and DPI commands");
            hold.SetResult(true); await Task.Delay(100); hold = null; quick.UpdateLayout();
            Check(first.Current == smaller && Equals(List("quick-output-list-1").SelectedItem, smaller) && List("quick-output-list-1").IsEnabled, "Completed mode change selects actual current output");
            Toggle("quick-refresh-toggle-1"); rates = List("quick-refresh-list-1");
            Check(!Has("quick-output-list-1") && rates.Items.Count == 1 && Equals(rates.SelectedItem, smaller), "Refresh choices follow the newly selected output resolution");
            Toggle("quick-output-toggle-1"); fail = true; List("quick-output-list-1").SelectedItem = current; quick.UpdateLayout();
            Check(calls == 2 && first.Current == smaller && Equals(List("quick-output-list-1").SelectedItem, smaller) && Descendants<TextBlock>(quick).Any(t => t.Text == "测试：输出模式被拒绝"), "Rejected output selection reports inline and returns to actual mode");
            quick.Render(); quick.UpdateLayout();
            Check(Descendants<TextBlock>(quick).Any(t => t.Text == "测试：输出模式被拒绝") && !window.IsVisible, "Mode error survives rerender while main settings stays hidden");
            fail = false; List("quick-output-list-1").SelectedItem = slower; quick.UpdateLayout();
            Toggle("quick-refresh-toggle-1"); List("quick-refresh-list-1").SelectedItem = current; quick.UpdateLayout();
            Check(calls == 4 && first.Current == current && Equals(requestedMode, current) && !Descendants<TextBlock>(quick).Any(t => t.Text == "测试：输出模式被拒绝"), "Refresh selection applies directly and successful mode change clears prior error");
            var rotation = Descendants<ComboBox>(quick).Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "quick-rotation-1");
            rotation.SelectedIndex = 2; await Task.Delay(40);
            Check(calls == 5 && ReferenceEquals(requestedDisplay, first) && requestedOrientation == 2 && requestedMode == current,
                "Quick rotation uses the selected display and existing validated mode transaction");
            SelectQuick(quick, 2);
            Check(!Has("quick-output-toggle-2") && !Has("quick-refresh-toggle-2") && !Descendants<TextBlock>(quick).Any(t => t.Text == "显示器音量"), "Unsupported display has no fabricated output, refresh or volume controls");
            Check(!Has("quick-volume-down") && !Has("quick-volume-up"), "Selecting an unsupported monitor removes volume steps");
            Check(!window.IsVisible, "All quick mode operations leave main settings hidden");
        }
        finally
        {
            hold?.TrySetResult(true); await Task.Delay(50); quick.Close(); window.Displays = real; window.Preferences.QuickMoreDisplayId = savedMore; window.SavePreferences(); window.Show();
        }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) yield return found;
            foreach (var sub in Descendants<T>(child)) yield return sub;
        }
    }
    private static void Snapshot(FrameworkElement window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
    }
}
