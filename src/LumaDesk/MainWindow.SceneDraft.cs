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
    // Session-only: never put a draft in Preferences until the user saves it.
    private Preset? sceneDraft;

    private async Task<Preset> CaptureLiveSceneDraftAsync()
    {
        await Task.WhenAll(brightnessGates.Values.ToArray().Select(async gate =>
        {
            await gate.WaitAsync();
            gate.Release();
        }));
        // Scan directly so a failed scan cannot silently capture the previous inventory.
        var displays = await Service.ScanAsync();
        foreach (var display in displays)
        {
            display.Dimming = Displays.FirstOrDefault(d => string.Equals(d.Id, display.Id, StringComparison.OrdinalIgnoreCase))?.Dimming ?? 0;
            ApplyAlias(display);
        }
        return await SceneService.CaptureDraft(new SceneBackend(this, displays));
    }

    internal Button CreateSceneCaptureButton(Func<Task<Preset>>? capture = null)
    {
        var button = Ui.Button(L.T("scene.captureDraft"), () => _ = RunAsync(() => CaptureSceneDraftAsync(capture ?? CaptureLiveSceneDraftAsync)));
        button.IsEnabled = sceneDraft == null && !sceneBusy;
        return button;
    }

    internal async Task CaptureSceneDraftAsync(Func<Task<Preset>> capture)
    {
        if (sceneDraft != null) return;
        if (sceneBusy || modePending || scanning || Service.IsWriting) { Status(L.T("s92f637408f"), true); return; }
        sceneBusy = true;
        using var hold = quick?.KeepOpen();
        var windows = System.Windows.Application.Current.Windows.Cast<Window>().Where(w => w.IsEnabled).ToArray();
        foreach (var window in windows) window.IsEnabled = false;
        try
        {
            sceneDraft = await capture();
            string baseName = sceneDraft.Name; int suffix = 2;
            while (Preferences.Presets.Any(p => p.Name.Equals(sceneDraft.Name, StringComparison.OrdinalIgnoreCase)))
                sceneDraft.Name = baseName + " " + suffix++;
            Status(L.T("scene.draftHelp"));
        }
        finally
        {
            sceneBusy = false;
            foreach (var window in windows) window.IsEnabled = true;
            Render(); quick?.Render();
        }
    }

    private StackPanel CreateSceneDraftEditor(Preset draft)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Labels(L.T("scene.draftTitle"), L.T("scene.draftHelp")));
        foreach (var display in draft.Displays)
            panel.Children.Add(Ui.Labels(display.Name, L.F("scene.draftSummary", display.Mode,
                display.Brightness?.ToString() ?? "—", display.Dimming, display.Dpi?.ToString() ?? "—")));
        var name = new TextBox { Text = draft.Name, MaxLength = 40, MinWidth = 180 };
        System.Windows.Automation.AutomationProperties.SetName(name, L.T("scene.duplicateName"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(name, "SceneDraftName");
        name.TextChanged += (_, _) => draft.Name = name.Text;
        panel.Children.Add(Ui.Row(name, Ui.Row(Ui.Button(L.T("scene.saveDraft"), () => SaveSceneDraft(draft), true),
            Ui.Button(L.T("scene.discardDraft"), () =>
            {
                if (sceneBusy || sceneDraft != draft) return;
                sceneDraft = null; Render();
            }))));
        return panel;
    }

    private void SaveSceneDraft(Preset draft)
    {
        if (sceneBusy || sceneDraft != draft) return;
        Preset saved;
        // Reuse the independent-copy and name-validation rules, without recapturing hardware.
        try { saved = SceneService.Duplicate(draft, draft.Name, Preferences.Presets); }
        catch (ArgumentException ex) { Status(ex.Message, true); return; }
        Preferences.Presets.Add(saved);
        try { Storage.Save(Preferences); }
        catch (Exception ex)
        {
            Preferences.Presets.Remove(saved);
            Status(L.T("s888403f2f3") + ex.Message, true);
            return;
        }
        sceneDraft = null;
        Render(); quick?.Render(); Status(L.T("sa0d6e1ed82"));
    }
}
