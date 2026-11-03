// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LumaDesk;

internal sealed class LayoutWindow : Window
{
    private readonly MainWindow owner;
    private readonly Func<IReadOnlyList<ScreenPlacement>, Task> apply;
    private readonly Canvas canvas = new() { Height = 260, ClipToBounds = true, Background = Ui.Brush("#E8EDF5"), Margin = new Thickness(0, 14, 0, 14) };
    private readonly StackPanel body = new() { Margin = new Thickness(22) };
    private readonly TextBlock feedback = Ui.Text(L.T("s41463881b8"), 12, "#64748B");
    private readonly TextBox xBox = new() { Width = 95 }, yBox = new() { Width = 95 };
    private readonly ComboBox selected = new() { DisplayMemberPath = "Name", Width = 185 };
    private readonly ComboBox primary = new() { DisplayMemberPath = "Name", Width = 185 };
    private List<ScreenPlacement> placements = new();
    private List<DisplayInfo> displays = new();
    private bool loading, busy;
    private double scale, offsetX, offsetY;
    internal IReadOnlyList<ScreenPlacement> Placements => placements;
    internal LayoutWindow(MainWindow owner, Func<IReadOnlyList<ScreenPlacement>, Task>? apply = null)
    {
        Ui.ConfigureWindow(this);
        this.owner = owner; this.apply = apply ?? (p => owner.ChangeLayoutAsync(p));
        Title = L.T("sc91a2a81a1"); Width = 760; Height = 650; MinWidth = 580; MinHeight = 530; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer { Content = body };
        body.Children.Add(Ui.Text(L.T("s307f94d8e9"), 22, "#26364A", true));
        body.Children.Add(canvas);
        var coordinates = new StackPanel { Orientation = Orientation.Horizontal };
        coordinates.Children.Add(selected); coordinates.Children.Add(Ui.Text("  X  ", 12)); coordinates.Children.Add(xBox); coordinates.Children.Add(Ui.Text("  Y  ", 12)); coordinates.Children.Add(yBox);
        var update = Ui.Button(L.T("s4a05c9ce76"), UpdateCoordinates); update.Margin = new Thickness(8, 0, 0, 0); coordinates.Children.Add(update); body.Children.Add(coordinates);
        var primaryRow = Ui.Row(Ui.Text(L.T("s93ff5822ca"), 13), primary); primaryRow.Margin = new Thickness(0, 14, 0, 14); body.Children.Add(primaryRow);
        var arrange = new StackPanel { Orientation = Orientation.Horizontal };
        arrange.Children.Add(Ui.Button(L.T("se25efd6551"), () => Arrange(false))); var vertical = Ui.Button(L.T("s86d5bec31e"), () => Arrange(true)); vertical.Margin = new Thickness(8, 0, 8, 0); arrange.Children.Add(vertical);
        arrange.Children.Add(Ui.Button(L.T("sdf7392cc96"), () => _ = ExecuteAsync(async () => { await owner.RefreshAsync(); Reload(); })));
        body.Children.Add(arrange);
        var topology = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        topology.Children.Add(Ui.Button(L.T("sa60416dbac"), () => _ = TopologyAsync(true))); var clone = Ui.Button(L.T("s99c94fd883"), () => _ = TopologyAsync(false)); clone.Margin = new Thickness(8, 0, 0, 0); topology.Children.Add(clone); body.Children.Add(topology);
        feedback.Margin = new Thickness(0, 7, 0, 14); body.Children.Add(feedback);
        var applyButton = Ui.Button(L.T("seb82fd9848"), () => _ = ExecuteAsync(async () =>
        {
            var normalized = LayoutRules.Normalize(placements); await this.apply(normalized); Reload(); feedback.Text = L.T("s2128b7d275");
        }), true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(applyButton, "layout-apply");
        body.Children.Add(Ui.Row(Ui.Text(L.T("s67773de970"), 11, "#718096"), applyButton));
        selected.SelectionChanged += (_, _) => UpdateFields();
        primary.SelectionChanged += (_, _) =>
        {
            if (loading || primary.SelectedItem is not DisplayInfo display) return;
            placements = placements.Select(p => p with { Primary = p.Id == display.Id }).ToList(); Draw();
        };
        canvas.SizeChanged += (_, _) => Draw();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Reload();
    }
    private void Reload()
    {
        loading = true; displays = owner.Displays.ToList();
        placements = displays.Select(d => new ScreenPlacement(d.Id, d.X, d.Y, (int)d.Current.Width, (int)d.Current.Height, d.Primary)).ToList();
        selected.ItemsSource = displays; selected.SelectedIndex = displays.Count > 0 ? 0 : -1;
        primary.ItemsSource = displays; primary.SelectedItem = displays.FirstOrDefault(d => d.Primary);
        loading = false; UpdateFields(); Draw();
    }
    private void UpdateFields()
    {
        if (selected.SelectedItem is not DisplayInfo d) return;
        var p = placements.FirstOrDefault(i => i.Id == d.Id); if (p == null) return;
        xBox.Text = p.X.ToString(); yBox.Text = p.Y.ToString();
    }
    private void UpdateCoordinates()
    {
        if (selected.SelectedItem is not DisplayInfo d) return;
        if (!int.TryParse(xBox.Text, out int x) || !int.TryParse(yBox.Text, out int y) || Math.Abs((long)x) > 65535 || Math.Abs((long)y) > 65535)
        { feedback.Text = L.T("s444a27d2e4"); return; }
        placements = placements.Select(p => p.Id == d.Id ? p with { X = x, Y = y } : p).ToList(); Draw();
    }
    private void Arrange(bool vertical)
    {
        int cursor = 0;
        placements = placements.Select(p => { var next = p with { X = vertical ? 0 : cursor, Y = vertical ? cursor : 0 }; cursor += vertical ? p.Height : p.Width; return next; }).ToList();
        UpdateFields(); Draw();
    }
    private async Task TopologyAsync(bool extend) => await ExecuteAsync(async () =>
    {
        await owner.ChangeTopologyAsync(extend); Reload(); feedback.Text = extend ? L.T("s181ae3be3a") : L.T("sad03744b01");
    });
    private async Task ExecuteAsync(Func<Task> action)
    {
        if (busy) return; busy = true; body.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { feedback.Text = ex.Message; owner.Status(ex.Message, true); }
        finally { busy = false; body.IsEnabled = true; }
    }
    private void Draw()
    {
        canvas.Children.Clear(); if (placements.Count == 0 || canvas.ActualWidth < 1) return;
        int minX = placements.Min(p => p.X), minY = placements.Min(p => p.Y);
        double width = placements.Max(p => (double)p.X + p.Width) - minX, height = placements.Max(p => (double)p.Y + p.Height) - minY;
        scale = Math.Min((canvas.ActualWidth - 48) / Math.Max(1, width), (canvas.Height - 48) / Math.Max(1, height));
        offsetX = (canvas.ActualWidth - width * scale) / 2 - minX * scale; offsetY = (canvas.Height - height * scale) / 2 - minY * scale;
        foreach (var placement in placements.ToArray())
        {
            var display = displays.Single(d => d.Id == placement.Id);
            var label = Ui.Text(display.Number + "  " + display.Name + (placement.Primary ? L.T("scef99cd639") : ""), 12, placement.Primary ? "#FFFFFF" : "#334155", true); label.HorizontalAlignment = HorizontalAlignment.Center;
            var box = new Border { Width = placement.Width * scale, Height = placement.Height * scale, Background = Ui.Brush(placement.Primary ? "#4685EC" : "#CBD7E9"), BorderBrush = Ui.Brush("#FFFFFF"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Child = label, Cursor = Cursors.SizeAll, ToolTip = display.Name + " · " + display.Current.Resolution };
            Canvas.SetLeft(box, offsetX + placement.X * scale); Canvas.SetTop(box, offsetY + placement.Y * scale); canvas.Children.Add(box);
            System.Windows.Automation.AutomationProperties.SetAutomationId(box, "layout-screen-" + display.Number);
            Point origin = default; bool dragging = false;
            box.MouseLeftButtonDown += (_, e) => { selected.SelectedItem = display; origin = e.GetPosition(canvas); dragging = box.CaptureMouse(); e.Handled = true; };
            box.MouseMove += (_, e) =>
            {
                if (!dragging) return; Point point = e.GetPosition(canvas);
                Canvas.SetLeft(box, offsetX + placement.X * scale + point.X - origin.X); Canvas.SetTop(box, offsetY + placement.Y * scale + point.Y - origin.Y);
            };
            box.MouseLeftButtonUp += (_, e) =>
            {
                if (!dragging) return; dragging = false; box.ReleaseMouseCapture(); Point point = e.GetPosition(canvas);
                var moved = placement with { X = placement.X + (int)Math.Round((point.X - origin.X) / scale), Y = placement.Y + (int)Math.Round((point.Y - origin.Y) / scale) };
                moved = LayoutRules.Snap(moved, placements, (int)(16 / scale));
                placements = placements.Select(p => p.Id == moved.Id ? moved : p).ToList(); UpdateFields(); Draw(); e.Handled = true;
            };
        }
    }
}
