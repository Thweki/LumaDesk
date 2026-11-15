// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal Window CreateSceneApplyPreviewDialog(string name, SceneApplyPreview preview)
    {
        var dialog = new Window
        {
            Title = L.T("scene.previewApply"), Width = 520, Height = 440,
            WindowStartupLocation = IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false
        };
        if (IsVisible) dialog.Owner = this;
        Ui.ConfigureWindow(dialog);
        var root = new DockPanel { Margin = new Thickness(18) };
        var apply = Ui.Button(L.T("s63c73c4730"), () => dialog.DialogResult = true, true);
        apply.IsEnabled = preview.ConnectedCount > 0;
        var cancel = Ui.Button(L.T("scene.previewCancel"), () => dialog.DialogResult = false);
        cancel.IsCancel = true; cancel.IsDefault = true;
        var buttons = Ui.Row(cancel, apply);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var content = new StackPanel();
        void Line(string text, bool bold = false, string? id = null)
        {
            var label = Ui.Text(text, bold ? 14 : 12, bold: bold);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(0, 0, 0, 10); label.ToolTip = id;
            content.Children.Add(label);
        }
        Line(name, true);
        Line(L.F("scene.previewSummary", preview.ConnectedCount, preview.Targets.Count - preview.ConnectedCount));
        Line(L.T("scene.previewHelp"));
        foreach (var target in preview.Targets)
        {
            Line(target.Name, true, target.Id);
            Line(target.Connected ? target.Settings : L.T("scene.disconnected"));
        }
        if (preview.HasLayout) Line(L.T("scene.previewLayout"));
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = root;
        return dialog;
    }
}
