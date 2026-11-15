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
                    var owner = new MainWindow();
                    TestSceneDuplicateFixture(owner);
                    TestSceneOrderFixture(owner);
                    await TestLiveBrightnessFixtureAsync(owner);
                    await TestInputSwitchUiAsync(owner, offscreen: true);
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
