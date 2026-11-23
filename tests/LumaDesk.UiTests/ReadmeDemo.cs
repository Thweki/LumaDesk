// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;

internal static partial class Program
{
    private static FrameworkElement ArrangeContent(Window window, double width, double height)
    {
        var surface = (FrameworkElement)window.Content;
        surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height)); surface.UpdateLayout();
        return surface;
    }

    private static int RunReadmeDemo(App app)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await CaptureReadmeDemoAsync(); app.Shutdown(0); }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        }));
        return app.Run();
    }

    // Render detached production controls so the capture never starts a tray, screen scan or native popup.
    private static async Task CaptureReadmeDemoAsync()
    {
        var window = new MainWindow(() => true);
        window.Displays = new()
        {
            new() { Id = "demo-left", Number = 1, Name = "Studio 27", X = -2560, Current = new(2560, 1440, 144), Brightness = 60, DpiScale = DpiRules.FromRelative(0, 0, 2) },
            new() { Id = "demo-main", Number = 2, Name = "Studio 32", Primary = true, Current = new(3840, 2160, 60), Brightness = 60, DpiScale = DpiRules.FromRelative(0, 1, 2) },
            new() { Id = "demo-portrait", Number = 3, Name = "Portrait", X = 3840, Current = new(1440, 2560, 60), Brightness = 60, DpiScale = DpiRules.FromRelative(0, 0, 2) }
        };
        window.Preferences.QuickSelectedDisplayId = "demo-main";
        window.Preferences.QuickMoreDisplayId = null;
        window.Preferences.SyncBrightness = false;
        foreach (var display in window.Displays) display.Modes.Add(display.Current);
        window.RefreshView();
        window.StatusLabel.Text = L.F("sd1362bfd3f", window.Displays.Count, new DateTime(2026, 1, 1, 10, 30, 0));
        Snapshot(ArrangeContent(window, 960, 700), "settings.png");
        Task SetBrightness(DisplayInfo display, int value)
        {
            var split = BrightnessRules.Split(value, true);
            display.Brightness = split.Hardware; display.Dimming = split.Dimming;
            return Task.CompletedTask;
        }
        var quick = new QuickWindow(window, (_, _, _) => Task.CompletedTask, dismissOnDeactivate: false,
            applyBrightness: SetBrightness, readHdrActive: _ => Task.FromResult(false),
            applySelectedBrightness: async (ids, value) =>
            {
                foreach (var display in window.Displays.Where(d => ids.Contains(d.Id))) await SetBrightness(display, value);
            });
        int frame = 0;
        using var timeline = new StreamWriter(Path.Combine(output, "frames.tsv"));
        async Task Frame(int duration, string stage)
        {
            await Task.Delay(100);
            var surface = ArrangeContent(quick, quick.Width, quick.Height);
            string name = $"frame-{frame++:D3}.png";
            if (stage == "menu") SnapshotDisplayMenu(surface, name);
            else Snapshot(surface, name);
            var brightness = Descendants<Slider>(surface)
                .OrderBy(s => s.TranslatePoint(new Point(), surface).Y).First();
            var bottom = brightness.TranslatePoint(new Point(0, brightness.ActualHeight), surface).Y;
            timeline.WriteLine($"{name}\t{duration}\t{stage}\t{(int)Math.Ceiling(bottom)}");
        }
        void Press(string id) => PressQuickAction(quick, id);
        quick.Render();
        await Frame(1600, "select"); Snapshot((FrameworkElement)quick.Content, "quick-panel.png");
        Press("quick-display-select-1"); await Frame(1000, "select");
        Press("quick-display-select-3"); await Frame(1000, "select");
        Press("quick-display-select-2"); await Frame(1800, "menu");
        Press("quick-multi-toggle"); await Frame(1000, "link");
        Press("quick-display-select-1"); await Frame(800, "link");
        Press("quick-display-select-3"); await Frame(800, "link");
        var slider = Descendants<Slider>((FrameworkElement)quick.Content).Single(s => AutomationProperties.GetAutomationId(s) == "quick-multi-brightness");
        slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        foreach (int value in Enumerable.Range(0, 25).Select(i => 68 - i * 2))
        {
            slider.Value = value; await Frame(70, "adjust");
        }
        foreach (int value in Enumerable.Range(0, 21).Select(i => 20 + i * 3))
        {
            slider.Value = value; await Frame(70, "adjust");
        }
        slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        await Frame(1600, "adjust");
        Press("quick-multi-toggle"); await Frame(1000, "done");
        ThemeService.Apply("dark"); await Frame(1800, "theme"); Snapshot((FrameworkElement)quick.Content, "quick-panel-dark.png");
        ThemeService.Apply("light"); await Frame(800, "done");
        Check(new WindowInteropHelper(quick).Handle == IntPtr.Zero && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "README capture uses detached WPF controls");
    }

    private static void SnapshotDisplayMenu(FrameworkElement surface, string name)
    {
        var trigger = Descendants<Button>(surface).Single(b => AutomationProperties.GetAutomationId(b) == "quick-display-actions");
        var menu = trigger.ContextMenu;
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout();
        var frame = new Canvas { Width = surface.ActualWidth, Height = surface.ActualHeight };
        frame.Children.Add(new Rectangle { Width = frame.Width, Height = frame.Height, Fill = new ImageBrush(Capture(surface)) });
        var anchor = trigger.TranslatePoint(new Point(trigger.ActualWidth, trigger.ActualHeight), surface);
        var popup = new Rectangle { Width = menu.ActualWidth, Height = menu.ActualHeight, Fill = new ImageBrush(Capture(menu)) };
        Canvas.SetLeft(popup, Math.Max(0, anchor.X - popup.Width)); Canvas.SetTop(popup, anchor.Y);
        frame.Children.Add(popup);
        frame.Measure(new Size(frame.Width, frame.Height)); frame.Arrange(new Rect(0, 0, frame.Width, frame.Height));
        Snapshot(frame, name);
    }
}
