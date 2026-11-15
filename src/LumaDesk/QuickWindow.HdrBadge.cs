// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LumaDesk;

internal sealed partial class QuickWindow
{
    private readonly Func<DisplayInfo, Task<bool>> readHdrActive;

    private static Action<bool> AddHdrBadge(Button button, Rect bounds)
    {
        string status = AutomationProperties.GetItemStatus(button);
        string name = AutomationProperties.GetName(button);
        var face = new Grid { Height = Math.Max(1, bounds.Height - 5), ClipToBounds = true };
        face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        face.RowDefinitions.Add(new RowDefinition());
        if (button.Content is UIElement content)
        {
            button.Content = null;
            if (content is Grid multiFace) multiFace.Height = double.NaN;
            Grid.SetRow(content, 1);
            face.Children.Add(content);
        }
        var badge = new Border
        {
            Background = Ui.Brush("#E6F2F4"), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(3, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, Child = Ui.Text("HDR", 9, "#007A8A", true),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(badge, AutomationProperties.GetAutomationId(button) + "-hdr");
        AutomationProperties.SetName(badge, L.T("s166778a827"));
        face.Children.Add(badge); button.Content = face;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return active =>
        {
            // Tiny map tiles retain status in their tooltip and accessibility text.
            badge.Visibility = active && bounds.Width >= 42 && bounds.Height >= 48 ? Visibility.Visible : Visibility.Collapsed;
            string hdr = active ? L.T("s166778a827") : "";
            button.ToolTip = active ? name + " · " + hdr : name;
            AutomationProperties.SetItemStatus(button, active ? (status.Length == 0 ? hdr : status + " · " + hdr) : status);
        };
    }

    private void ObserveHdrBadges(Canvas canvas, DisplayInfo[] displays, HashSet<string> states, Dictionary<string, Action<bool>> updates)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        bool busy = false;
        async Task Refresh()
        {
            if (busy || closed || !canvas.IsLoaded) return;
            busy = true;
            try
            {
                foreach (var display in displays)
                {
                    bool active;
                    try { active = await readHdrActive(display); }
                    catch { active = false; }
                    // A late probe belongs only to this map, never a replacement map.
                    if (closed || !canvas.IsLoaded) return;
                    if (active) states.Add(display.Id); else states.Remove(display.Id);
                    if (updates.TryGetValue(display.Id, out var update)) update(active);
                }
            }
            finally { busy = false; }
        }
        timer.Tick += async (_, _) => await Refresh();
        canvas.Loaded += async (_, _) => { timer.Start(); await Refresh(); };
        canvas.Unloaded += (_, _) => timer.Stop();
    }
}
