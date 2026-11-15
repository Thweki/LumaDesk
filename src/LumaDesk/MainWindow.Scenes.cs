// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    private bool sceneBusy;
    private Preset? undoScene;
    private string sceneReport = "";
    private sealed class SceneBackend : ISceneBackend
    {
        private readonly MainWindow owner;
        internal SceneBackend(MainWindow owner) => this.owner = owner;
        public IReadOnlyList<DisplayInfo> Displays => owner.Displays;
        public Task<DisplayColorState> Color(DisplayInfo d) => Task.Run(() => AdvancedColorService.Read(d));
        public Task Mode(DisplayInfo d, DisplayMode mode, uint orientation) => owner.ChangeModeAsync(d, mode, orientation, exactOutput: true);
        public Task Dpi(DisplayInfo d, int percent) => owner.ChangeDpiAsync(d, percent);
        public async Task Hdr(DisplayInfo d, bool enabled)
        {
            await Task.Run(() => AdvancedColorService.SetHdr(d, enabled));
            await owner.RefreshAsync(sceneRefresh: true);
        }
        public Task Layout(IReadOnlyList<ScreenPlacement> placements) => owner.ChangeLayoutAsync(placements);
        public Task Profiles(DisplayInfo d, PresetDisplay saved) => Task.Run(() =>
            AdvancedColorService.RestoreProfiles(d, saved.UserProfiles, saved.SdrProfile, saved.HdrProfile));
        public Task Brightness(DisplayInfo d, PresetDisplay saved)
        {
            owner.CancelReconnect(d.Id);
            return owner.RestoreBrightnessAsync(d, saved);
        }
    }

    private async Task SaveSceneAsync(string name, Preset? replace = null)
    {
        if (sceneBusy || modePending || scanning || Service.IsWriting) { Status(L.T("s92f637408f"), true); return; }
        if (string.IsNullOrWhiteSpace(name) || Displays.Count == 0) { Status(L.T("scene.nameRequired"), true); return; }
        if (Preferences.Presets.Any(p => p != replace && p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        { Status(L.T("s1f09d8e896"), true); return; }
        sceneBusy = true; PageContent.IsEnabled = false;
        using var hold = quick?.KeepOpen();
        var windows = System.Windows.Application.Current.Windows.Cast<Window>().Where(w => w.IsEnabled).ToArray();
        foreach (var window in windows) window.IsEnabled = false;
        try
        {
            await RefreshAsync(sceneRefresh: true);
            var scene = await SceneService.Capture(new SceneBackend(this), name.Trim());
            if (replace == null) Preferences.Presets.Add(scene);
            else { replace.Name = scene.Name; replace.Displays = scene.Displays; }
            Save(); Render(); quick?.Render(); Status(L.T("sa0d6e1ed82"));
        }
        finally
        {
            sceneBusy = false; PageContent.IsEnabled = true;
            foreach (var window in windows) window.IsEnabled = true;
            hold?.Dispose(); quick?.Render();
        }
    }

    internal async Task<SceneResult?> ApplySceneAsync(Preset preset, bool undo = false)
    {
        if (sceneBusy || modePending || scanning || Service.IsWriting) throw new InvalidOperationException(L.T("s92f637408f"));
        sceneBusy = true;
        using var hold = quick?.KeepOpen();
        var windows = System.Windows.Application.Current.Windows.Cast<Window>().Where(w => w.IsEnabled).ToArray();
        foreach (var window in windows) window.IsEnabled = false;
        try
        {
            await RefreshAsync(sceneRefresh: true);
            var backend = new SceneBackend(this);
            var before = await SceneService.Capture(backend, L.T("scene.previous"));
            if (!undo) undoScene = before;
            var result = await SceneService.Apply(backend, preset);
            if (!undo) DisplayResets.Remember(preset, result);
            if (undo && result.Issues.Count == 0) undoScene = null;
            sceneReport = string.Join("\n", result.Issues);
            Save();
            string message = L.F("sa40a67f8f2", preset.Name, result.Applied);
            if (result.Issues.Count > 0) message += " · " + L.T("scene.partial");
            Status(message, result.Issues.Count > 0); StatusLabel.ToolTip = sceneReport;
            if (result.Issues.Count > 0 && !IsVisible) tray?.ShowBalloonTip(5000, AppBrand.Name, message + "\n" + sceneReport, System.Windows.Forms.ToolTipIcon.Warning);
            return result;
        }
        finally
        {
            sceneBusy = false;
            foreach (var window in windows) window.IsEnabled = true;
            hold?.Dispose();
            Render(); quick?.Render();
        }
    }

    internal Button SceneMenuButton()
    {
        var button = Ui.Button(L.T("sf1de1e2621"), () => { });
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var preset in Preferences.Presets)
            {
                var item = new MenuItem { Header = preset.Name, IsEnabled = !sceneBusy };
                item.Click += (_, _) => _ = RunAsync(async () => await ApplySceneAsync(preset)); menu.Items.Add(item);
            }
            if (undoScene != null)
            {
                menu.Items.Add(new Separator());
                var item = new MenuItem { Header = L.T("scene.undo"), IsEnabled = !sceneBusy };
                item.Click += (_, _) => _ = RunAsync(async () => { if (undoScene != null) await ApplySceneAsync(undoScene, true); }); menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var manage = new MenuItem { Header = L.T("scene.manage") };
            manage.Click += (_, _) => { page = "presets"; Render(); ShowSettings(); }; menu.Items.Add(manage);
            menu.PlacementTarget = button; button.ContextMenu = menu; menu.IsOpen = true;
        };
        return button;
    }

    internal StackPanel CreateSceneDuplicateEditor(Preset source)
    {
        var name = new TextBox { MaxLength = 40, MinWidth = 180 };
        System.Windows.Automation.AutomationProperties.SetName(name, L.T("scene.duplicateName"));
        var panel = new StackPanel();
        panel.Children.Add(Ui.Labels(L.T("scene.duplicateName"), L.T("scene.duplicateHelp")));
        panel.Children.Add(Ui.Row(name, Ui.Button(L.T("scene.duplicate"), () => DuplicateScene(source, name.Text))));
        return panel;
    }

    private void DuplicateScene(Preset source, string name)
    {
        Preset copy;
        try { copy = SceneService.Duplicate(source, name, Preferences.Presets); }
        catch (ArgumentException ex) { Status(ex.Message, true); return; }
        Preferences.Presets.Add(copy);
        try { Storage.Save(Preferences); }
        catch (Exception ex)
        {
            Preferences.Presets.Remove(copy);
            Status(L.T("s888403f2f3") + ex.Message, true);
            return;
        }
        Render(); quick?.Render(); Status(L.T("sa0d6e1ed82"));
    }

    private StackPanel CreateSceneOrderControls(Preset scene)
    {
        int index = Preferences.Presets.IndexOf(scene);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var up = Ui.Button(L.T("panel.up"), () => MoveScene(scene, -1));
        var down = Ui.Button(L.T("panel.down"), () => MoveScene(scene, 1));
        up.IsEnabled = index > 0 && !sceneBusy;
        down.IsEnabled = index >= 0 && index < Preferences.Presets.Count - 1 && !sceneBusy;
        System.Windows.Automation.AutomationProperties.SetName(up, scene.Name + ": " + L.T("panel.up"));
        System.Windows.Automation.AutomationProperties.SetName(down, scene.Name + ": " + L.T("panel.down"));
        controls.Children.Add(up); controls.Children.Add(down);
        return controls;
    }

    private void MoveScene(Preset scene, int direction)
    {
        if (sceneBusy || !SceneService.Move(Preferences.Presets, scene, direction)) return;
        try { Storage.Save(Preferences); }
        catch (Exception ex)
        {
            SceneService.Move(Preferences.Presets, scene, -direction);
            Status(L.T("s888403f2f3") + ex.Message, true);
            return;
        }
        Render(); quick?.Render(); Status(L.T("sa0d6e1ed82"));
    }

    private void RenderScenes()
    {
        Heading(L.T("s0b2fca3986"), L.T("scene.description"));
        string defaultName = L.T("s3cbfca23b6"); int suffix = 2;
        while (Preferences.Presets.Any(p => p.Name == defaultName)) defaultName = L.T("s3cbfca23b6") + " " + suffix++;
        var name = new TextBox { Text = defaultName, MaxLength = 40, MinWidth = 180 };
        PageContent.Children.Add(Ui.Card(Ui.Row(name, Ui.Button(L.T("sce211a5a47"), () => _ = RunAsync(() => SaveSceneAsync(name.Text)), true))));
        if (undoScene != null) PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Text(L.T("scene.previous")), Ui.Button(L.T("scene.undo"), () => _ = RunAsync(async () => { if (undoScene != null) await ApplySceneAsync(undoScene, true); })))));
        if (!string.IsNullOrEmpty(sceneReport)) PageContent.Children.Add(Ui.Card(new Expander { Header = L.T("scene.details"), Content = Ui.Text(sceneReport, 12) }));
        if (Preferences.Presets.Count == 0) PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s502f445e9e"), L.T("scene.empty"))));
        foreach (var scene in Preferences.Presets.ToArray())
        {
            var card = new StackPanel();
            card.Children.Add(Ui.Row(Ui.Labels(scene.Name, L.F("sb91b8e2562", scene.Displays.Count)), Ui.Button(L.T("s63c73c4730"), () => _ = RunAsync(async () => await ApplySceneAsync(scene)), true)));
            card.Children.Add(CreateSceneOrderControls(scene));
            var details = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var rename = new TextBox { Text = scene.Name, MaxLength = 40 };
            details.Children.Add(Ui.Row(rename, Ui.Button(L.T("scene.update"), () => _ = RunAsync(() => SaveSceneAsync(rename.Text, scene)))));
            details.Children.Add(Ui.Line());
            details.Children.Add(CreateSceneDuplicateEditor(scene));
            details.Children.Add(Ui.Line());
            details.Children.Add(Ui.Row(Ui.Labels(L.T("scene.onConnect"), L.T("scene.onConnectHelp")), Toggle(scene.OnConnect, v =>
            {
                if (v) foreach (var other in Preferences.Presets.Where(p => SceneService.SameScreens(p, scene.Displays.Select(s => new DisplayInfo { Id = s.Id })))) other.OnConnect = false;
                scene.OnConnect = v; Save(); Render();
            })));
            details.Children.Add(CreateSceneSchedule(scene));
            details.Children.Add(Ui.Line());
            details.Children.Add(Ui.Row(Ui.Text(L.T("s864200bbb2")), ShortcutEditor(scene.Shortcut, value => { scene.Shortcut = value; Save(); ConfigureHotkeys(); })));
            var delete = Ui.Button(L.T("s2f9daa8289"), () => { Preferences.Presets.Remove(scene); Save(); ConfigureHotkeys(); Render(); quick?.Render(); });
            delete.Margin = new Thickness(0, 12, 0, 0); details.Children.Add(delete);
            card.Children.Add(new Expander { Header = L.T("scene.more"), Content = details, Margin = new Thickness(0, 10, 0, 0) });
            PageContent.Children.Add(Ui.Card(card));
        }
    }
}
