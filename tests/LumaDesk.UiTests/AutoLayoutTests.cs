// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

internal static partial class Program
{
    private static void TestAutoLayoutFixture(MainWindow owner)
    {
        const string flag = "LUMADESK_EXPERIMENTAL_AUTOLAYOUT";
        string? saved = Environment.GetEnvironmentVariable(flag);
        var real = owner.Displays;
        var windows = new List<LayoutWindow>();
        int calls = 0;
        IReadOnlyList<ScreenPlacement>? received = null;
        FrameworkElement Content(LayoutWindow window)
        {
            windows.Add(window);
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(720, 650)); root.Arrange(new Rect(0, 0, 720, 650)); root.UpdateLayout();
            return root;
        }
        try
        {
            owner.Displays = new()
            {
                new DisplayInfo { Id = "a", Number = 1, Primary = true, Current = new(1920, 1080, 60) },
                new DisplayInfo { Id = "b", Number = 2, X = 2500, Y = 0, Current = new(1280, 1024, 60) }
            };
            Environment.SetEnvironmentVariable(flag, null);
            var disabled = Content(new LayoutWindow(owner, _ => { calls++; return Task.CompletedTask; }));
            Check(!Descendants<Button>(disabled).Any(b => AutomationProperties.GetAutomationId(b) == "layout-tidy"),
                "Tidy action is absent by default");

            Environment.SetEnvironmentVariable(flag, "1");
            var layout = new LayoutWindow(owner, p => { calls++; received = p; return Task.CompletedTask; });
            var content = Content(layout);
            var tidy = Descendants<Button>(content).Single(b => AutomationProperties.GetAutomationId(b) == "layout-tidy");
            Check(AutomationProperties.GetName(tidy) == L.T("layout.tidy.action"), "Tidy has a localized accessible name");
            Click(tidy);
            Check(calls == 0 && owner.Displays[1].X == 2500 && layout.Placements[1].X == 1920,
                "Tidy only edits the preview, leaving live display state and hardware untouched");
            Check(Descendants<TextBlock>(content).Any(t => t.Text == L.T("layout.tidy.preview")), "Tidy asks the user to review and apply");
            var preview = layout.Placements.ToArray();
            Click(tidy);
            Check(layout.Placements.SequenceEqual(preview) && calls == 0, "Repeat tidy is stable and still does not apply");
            Click(Descendants<Button>(content).Single(b => AutomationProperties.GetAutomationId(b) == "layout-apply"));
            Check(calls == 1 && received!.SequenceEqual(preview), "Explicit Apply submits the validated tidy preview through the existing callback");

            owner.Displays[0].Current = new(65535, 65535, 60);
            owner.Displays[1].Current = new(65535, 65535, 60);
            var impossible = new LayoutWindow(owner, _ => { calls++; return Task.CompletedTask; });
            var failedContent = Content(impossible);
            var original = impossible.Placements.ToArray();
            var failedTidy = Descendants<Button>(failedContent).Single(b => AutomationProperties.GetAutomationId(b) == "layout-tidy");
            Click(failedTidy);
            Check(impossible.Placements.SequenceEqual(original) && calls == 1 && failedTidy.IsEnabled &&
                Descendants<TextBlock>(failedContent).Any(t => t.Text == L.T("layout.tidy.unavailable")),
                "Failed tidy preserves the entire preview, reports the error and re-enables controls");
        }
        finally
        {
            foreach (var window in windows) window.Close();
            owner.Displays = real;
            Environment.SetEnvironmentVariable(flag, saved);
        }
    }
}
