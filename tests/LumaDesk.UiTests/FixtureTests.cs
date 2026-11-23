// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static partial class Program
{
    private static int RunFixtures(string[] args)
    {
        output = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/wpf-fixtures");
        Directory.CreateDirectory(output);
        void Result(string message)
        {
            File.WriteAllText(Path.Combine(output, "result.txt"), message + "\n");
            Console.WriteLine(message);
        }
        try
        {
            Result("RUNNING: desktop-independent WPF fixtures");
            if (args.Length > 2) throw new ArgumentException("Usage: --fixtures [output-directory]");
            Environment.SetEnvironmentVariable("LUMADESK_DATA_DIR", Path.Combine(output, "isolated-settings"));
            Storage.Save(new Preferences { Language = "zh-Hans", Theme = "light", Hotkeys = false, CloseToTray = false });
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var app = new App { IntegrationTest = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            app.DispatcherUnhandledException += (_, e) =>
            {
                Result($"FAIL after {checks} fixture checks: {e.Exception}");
                e.Handled = true;
                app.Shutdown(1);
            };
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += (_, _) =>
            {
                Result($"FAIL: fixture timeout after {checks} checks");
                app.Shutdown(1);
            };
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                int exitCode = 0;
                try
                {
                    // Never show the owner: Loaded starts discovery, tray, and automation.
                    // Only detached controls with injected hardware callbacks belong here.
                    // The logical-control fixtures need rendering on navigation and redraw
                    // without invoking the real window lifecycle.
                    var owner = new MainWindow(() => true);
                    TestSceneRenameFixture(owner);
                    TestSceneDuplicateFixture(owner);
                    await TestSceneDraftFixtureAsync(owner);
                    TestSceneOrderFixture(owner);
                    TestNextScheduleFixture(owner);
                    TestSceneNoteFixture(owner);
                    TestSceneSearchFixture(owner);
                    await TestLiveBrightnessFixtureAsync(owner);
                    await TestCompactBrightnessFixtureAsync(owner);
                    await TestInputSwitchUiAsync(owner, offscreen: true);
                    await TestInputConnectionUiAsync(owner);
                    await TestRefreshTimingUiAsync(owner);
                    TestDeferredSettingsRenderingFixture();
                    TestDetachedPanelLayout();
                    Check(!owner.IsLoaded && new WindowInteropHelper(owner).Handle == IntPtr.Zero && owner.Displays.Count == 0,
                        "Fixture owner never loads a desktop window or discovers displays");
                    Result($"PASS: {checks} desktop-independent WPF fixture checks. No hardware queries or writes.");
                }
                catch (Exception ex)
                {
                    exitCode = 1;
                    Result($"FAIL after {checks} fixture checks: {ex}");
                }
                finally { timeout.Stop(); app.Shutdown(exitCode); }
            }));
            timeout.Start();
            return app.Run();
        }
        catch (Exception ex)
        {
            Result($"FAIL after {checks} fixture checks: {ex}");
            return 1;
        }
    }

    private static void TestDetachedPanelLayout()
    {
        var primary = new DisplayInfo { Id = "desk", Number = 1, Name = "Studio 27", Primary = true,
            Current = new(3840, 2160, 120), Brightness = 64, HasDdc = true, DpiScale = DpiRules.FromRelative(-1, 0, 2) };
        primary.Modes.Add(primary.Current); primary.Modes.Add(new(3840, 2160, 60));
        var side = new DisplayInfo { Id = "side", Number = 2, Name = "Portrait 24", X = 3840,
            Current = new(1080, 1920, 60), Brightness = 48 };
        var owner = new MainWindow(() => true) { Displays = new() { primary, side } };
        owner.Preferences.QuickSelectedDisplayId = primary.Id;
        owner.Preferences.QuickMoreDisplayId = null;
        owner.RefreshView(); owner.StatusLabel.Text = L.F("sd1362bfd3f", 2, DateTime.Now);
        Snapshot(ArrangeContent(owner, 960, 700), "settings-overview.png");
        var displayRow = Descendants<Button>(owner.PageContent).Single(b =>
            System.Windows.Automation.AutomationProperties.GetName(b) == L.T("s84041df0ce") + primary.Name);
        Click(displayRow);
        Snapshot(ArrangeContent(owner, 860, 650), "display-settings.png");
        var apply = Descendants<Button>(owner.PageContent).Single(b => Equals(b.Content, L.T("s8a3a58dbb8")));
        var rate = Descendants<ComboBox>(owner.PageContent).Single(box => box.Items.Contains((uint)120));
        Check(!apply.IsEnabled, "Unchanged display mode has no pending apply action");
        rate.SelectedItem = (uint)60;
        Check(apply.IsEnabled, "Changing refresh rate enables the apply action");
        rate.SelectedItem = (uint)120;
        Check(!apply.IsEnabled, "Restoring the original rate clears the pending action");
        var quick = new QuickWindow(owner, readHdrActive: _ => Task.FromResult(false));
        quick.Render();
        var surface = ArrangeContent(quick, quick.Width, quick.Height);
        Snapshot(surface, "quick-panel.png");
        var body = Descendants<ScrollViewer>(surface).First();
        var pin = Descendants<Button>(surface).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == L.T("panel.pin"));
        Check(!body.IsAncestorOf(pin) && body.ScrollableHeight == 0, "Compact controls fit above a fixed toolbar");
        Check(new[] { "quick-multi-toggle", "quick-identify", "quick-move-panel" }.All(id => QuickMenuAction(quick, id) != null) &&
            Descendants<Button>(surface).Count(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "quick-display-actions") == 1,
            "The single display menu contains link, locate and move actions");
        PressQuickAction(quick, "quick-multi-toggle");
        Check(quick.IsBrightnessMultiSelect && QuickMenuAction(quick, "quick-move-panel") == null,
            "Linking opens multi-select and removes the single-display move action");
        PressQuickAction(quick, "quick-multi-toggle");
        Check(!quick.IsBrightnessMultiSelect, "Done returns directly to single-display brightness");
        Click(pin);
        Check(Equals(pin.ToolTip, L.T("panel.unpin")) && System.Windows.Automation.AutomationProperties.GetName(pin) == L.T("panel.unpin"),
            "Pinning keeps the toolbar button and updates its hover and accessibility labels");
        var brightness = Descendants<Slider>(owner.PageContent).First();
        Check(Equals(brightness.ToolTip, L.T("sa712089fbd")) && ToolTipService.GetShowsToolTipOnKeyboardFocus(brightness) == true &&
            !Descendants<TextBlock>(owner.PageContent).Any(t => t.Text == brightness.ToolTip as string),
            "Brightness guidance is available on hover and keyboard focus without permanent copy");
        ArrangeContent(owner, 740, 540);
        Check(owner.BrandName.Visibility == Visibility.Collapsed && owner.SidebarColumn.ActualWidth < 80,
            "Narrow settings keep usable content width with labeled icon navigation");
        Click(owner.Navigation.Children.OfType<Button>().First());
        ThemeService.Apply("dark");
        Snapshot(ArrangeContent(owner, 740, 540), "compact-dark.png");
        ThemeService.Apply("light");
        var pip = new PipWindow(primary);
        var pipSurface = ArrangeContent(pip, 340, 190);
        var pause = Descendants<Button>(pipSurface).Single(b => Equals(b.ToolTip, L.T("pip.pause")));
        Click(pause);
        Check(Equals(pause.ToolTip, L.T("pip.resume")) && new WindowInteropHelper(pip).Handle == IntPtr.Zero,
            "Picture-in-picture shows the next playback action without starting screen capture");
        Check(new WindowInteropHelper(quick).Handle == IntPtr.Zero && new WindowInteropHelper(owner).Handle == IntPtr.Zero,
            "Layout previews render without desktop windows");
    }

    private static async Task TestLiveBrightnessFixtureAsync(MainWindow window)
    {
        int writes = 0, latest = 0;
        var host = window.CreateSlider("live-fixture", 0, 100, v => { writes++; latest = v; return Task.CompletedTask; });
        var slider = Descendants<Slider>(host).Single();
        slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent }); slider.Value = 30; await Task.Delay(100);
        Check(writes == 1 && latest == 30, "Brightness ValueChanged reaches controller before drag release");
        slider.Value = 40; slider.Value = 70; await Task.Delay(100);
        Check(writes == 2 && latest == 70, "Live brightness coalesces rapid input to latest target");
        slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
    }
}
