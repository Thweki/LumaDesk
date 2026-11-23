// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    internal DisplayResetService DisplayResets { get; } = new();

    internal UIElement CreateDisplayResetControls(DisplayInfo display, Func<string, DisplayResetFields, Task>? reset = null)
    {
        reset ??= ResetDisplayAsync;
        var baseline = DisplayResets.Baseline(display.Id);
        var content = new StackPanel();
        content.Children.Add(Ui.Text(L.T("reset.title")));
        var source = Ui.Text(baseline == null ? L.T("reset.empty") : L.F("reset.source", baseline.PresetName), 11);
        source.TextWrapping = TextWrapping.Wrap; source.Margin = new Thickness(0, 5, 0, 0);
        content.Children.Add(source);
        if (baseline == null) return content;
        var help = Ui.Text(L.T("reset.help"), 11); help.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(help);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, fields) in new[] {
            ("scene.brightness", DisplayResetFields.Brightness), ("scene.scale", DisplayResetFields.Scale),
            ("s60cc99cc00", DisplayResetFields.Rotation), ("reset.all", baseline.Available) })
        {
            var button = Ui.Button(L.T(label), () => _ = RunAsync(() => reset(display.Id, fields)));
            button.IsEnabled = !sceneBusy && !modePending && (baseline.Available & fields) == fields;
            System.Windows.Automation.AutomationProperties.SetName(button, L.T("reset.title") + ": " + L.T(label));
            buttons.Children.Add(button);
        }
        content.Children.Add(buttons);
        return content;
    }

    private async Task ResetDisplayAsync(string id, DisplayResetFields fields)
    {
        if (sceneBusy || modePending || scanning || Service.IsWriting) throw new InvalidOperationException(L.T("s92f637408f"));
        sceneBusy = true;
        using var hold = quick?.KeepOpen();
        var windows = Application.Current.Windows.Cast<Window>().Where(w => w.IsEnabled).ToArray();
        foreach (var window in windows) window.IsEnabled = false;
        try
        {
            CancelReconnect(id);
            await RefreshAsync(sceneRefresh: true);
            var result = await DisplayResets.Reset(new SceneBackend(this), id, fields);
            Status(L.T(result.Issues.Count == 0 ? "reset.done" : "reset.partial"), result.Issues.Count > 0);
            StatusLabel.ToolTip = result.Issues.Count == 0 ? StatusLabel.Text : string.Join("\n", result.Issues);
        }
        finally
        {
            sceneBusy = false;
            foreach (var window in windows) window.IsEnabled = true;
            hold?.Dispose(); Render(); quick?.Render();
        }
    }
}
