// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    private bool sceneBusy;
    internal DisplayUndoHistory UndoHistory { get; } = new();
    private Preset? undoScene => UndoHistory.Snapshot;
    private string UndoLabel => L.T(UndoHistory.OutputOnly ? "scene.undoOutput" : "scene.undo");
    private string sceneReport = "";
    private string sceneSearch = "";
    private sealed class SceneBackend : ISceneBackend, ISceneGammaBackend
    {
        private readonly MainWindow owner;
        private readonly IReadOnlyList<DisplayInfo>? snapshot;
        internal SceneBackend(MainWindow owner, IReadOnlyList<DisplayInfo>? snapshot = null)
        { this.owner = owner; this.snapshot = snapshot; }
        public IReadOnlyList<DisplayInfo> Displays => snapshot ?? owner.Displays;
        public bool GammaEnabled => GammaService.Enabled;
        public Task<ushort[]?> ReadGamma(DisplayInfo d) => Task.Run(() => GammaService.Capture(d));
        public Task RestoreGamma(DisplayInfo d, ushort[] ramp) => Task.Run(() => GammaService.Restore(d, ramp));
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
        public async Task Contrast(DisplayInfo d, int percent)
        {
            await owner.Service.SetPercentAsync(d, 0x12, percent);
            d.Contrast = percent;
        }
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

    private void RenameScene(Preset scene, string name)
    {
        string previousName = scene.Name;
        try
        {
            SceneService.Rename(Preferences.Presets, scene, name);
            Storage.Save(Preferences);
        }
        catch (Exception ex)
        {
            scene.Name = previousName;
            Status(ex.Message, true);
            return;
        }
        Render(); quick?.Render(); Status(L.T("scene.renamed"));
    }

    internal async Task<SceneResult?> ApplySceneAsync(Preset preset, bool undo = false, string? displayId = null, bool preview = false)
    {
        if (sceneBusy || modePending || scanning || Service.IsWriting) throw new InvalidOperationException(L.T("s92f637408f"));
        sceneBusy = true;
        using var hold = quick?.KeepOpen();
        var windows = System.Windows.Application.Current.Windows.Cast<Window>().Where(w => w.IsEnabled).ToArray();
        foreach (var window in windows) window.IsEnabled = false;
        try
        {
            if (preview && !undo && SceneApplyPreview.Enabled &&
                !await SceneApplyPreview.ConfirmAsync(preset, async () =>
                {
                    var displays = await Service.ScanAsync();
                    foreach (var display in displays) ApplyAlias(display);
                    return displays;
                }, scope => CreateSceneApplyPreviewDialog(preset.Name, scope).ShowDialog() == true)) return null;
            await RefreshAsync(sceneRefresh: true);
            var backend = new SceneBackend(this);
            var target = displayId == null ? preset : SceneService.ForDisplay(preset, displayId);
            if (!undo)
            {
                var before = await SceneService.Capture(backend, L.T("scene.previous"));
                UndoHistory.Remember(displayId == null ? before : SceneService.ForDisplay(before, displayId));
            }
            var result = undo ? await UndoHistory.UndoAsync(backend) : await SceneService.ApplyAndRemember(backend, Preferences, target);
            if (result == null) return null;
            if (!undo) DisplayResets.Remember(target, result);
            if (displayId == null) sceneCycle.RecordApplied(preset, result, undo);
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
            var menu = CreateSceneMenu();
            menu.PlacementTarget = button; button.ContextMenu = menu; menu.IsOpen = true;
        };
        return button;
    }

    private string LastAppliedSceneText() => Preferences.LastPresetApplication is { } last
        ? L.F("scene.lastApplied", last.PresetName, last.AppliedAtUtc.ToLocalTime().ToString("G"))
        : L.T("scene.lastAppliedNone");

    internal ContextMenu CreateSceneMenu()
    {
        var menu = new ContextMenu();
        var summary = Ui.Text(LastAppliedSceneText());
        summary.TextWrapping = TextWrapping.Wrap; summary.MaxWidth = 360;
        var history = new MenuItem { Header = summary, IsEnabled = false, ToolTip = L.T("scene.lastAppliedHelp") };
        System.Windows.Automation.AutomationProperties.SetName(history, summary.Text);
        ToolTipService.SetShowOnDisabled(history, true);
        menu.Items.Add(history);
        menu.Items.Add(new Separator());
        foreach (var preset in Preferences.Presets)
        {
            var item = new MenuItem { Header = preset.Name, IsEnabled = !sceneBusy };
            if (SceneApplyPreview.Enabled) item.ToolTip = L.T("scene.previewApply");
            item.Click += (_, _) => _ = RunAsync(async () => await ApplySceneAsync(preset, preview: true)); menu.Items.Add(item);
        }
        if (undoScene != null)
        {
            menu.Items.Add(new Separator());
            var item = new MenuItem { Header = UndoLabel, IsEnabled = !sceneBusy };
            item.Click += (_, _) => _ = RunAsync(async () => { if (undoScene != null) await ApplySceneAsync(undoScene, true); }); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = L.T("scene.manage") };
        manage.Click += (_, _) => { page = "presets"; Render(); ShowSettings(); }; menu.Items.Add(manage);
        return menu;
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

    internal StackPanel CreateSceneDisplayEditor(Preset source, Func<Preset, string, Task>? apply = null)
    {
        apply ??= async (preset, id) => { await ApplySceneAsync(preset, displayId: id); };
        var picker = new ComboBox { MinWidth = 180 };
        System.Windows.Automation.AutomationProperties.SetName(picker, L.T("scene.targetDisplay"));
        foreach (var display in Displays.Where(d => source.Displays.Count(s => string.Equals(s.Id, d.Id, StringComparison.OrdinalIgnoreCase)) == 1))
            picker.Items.Add(new ComboBoxItem { Content = $"{display.Number} · {display.Name}", Tag = display.Id });
        var button = Ui.Button(L.T("scene.applyDisplay"), () =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: string id })
                _ = RunAsync(() => apply(source, id));
        });
        button.IsEnabled = false;
        picker.SelectionChanged += (_, _) => button.IsEnabled = picker.SelectedItem != null;
        picker.IsEnabled = picker.Items.Count > 0;
        var panel = new StackPanel();
        panel.Children.Add(Ui.Labels(L.T("scene.applyDisplay"), L.T(picker.Items.Count == 0 ? "scene.noMatchingDisplay" : "scene.applyDisplayHelp")));
        panel.Children.Add(Ui.Row(picker, button));
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
        if (GammaService.Enabled) PageContent.Children.Add(Ui.Card(Ui.Text(L.T("gamma.help"), 12)));
        var history = Ui.Labels(LastAppliedSceneText(), L.T("scene.lastAppliedHelp"));
        foreach (var text in history.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        PageContent.Children.Add(Ui.Card(history));
        string defaultName = L.T("s3cbfca23b6"); int suffix = 2;
        while (Preferences.Presets.Any(p => p.Name == defaultName)) defaultName = L.T("s3cbfca23b6") + " " + suffix++;
        var name = new TextBox { Text = defaultName, MaxLength = SceneService.MaxNameLength, MinWidth = 180 };
        var capture = CreateSceneCaptureButton();
        capture.HorizontalAlignment = HorizontalAlignment.Left; capture.Margin = new Thickness(0, 8, 0, 0);
        var create = new StackPanel();
        var nameLabel = new Label { Content = L.T("scene.duplicateName"), Target = name, Padding = new Thickness(0, 0, 0, 5) };
        System.Windows.Automation.AutomationProperties.SetLabeledBy(name, nameLabel);
        create.Children.Add(nameLabel);
        create.Children.Add(Ui.Row(name, Ui.Button(L.T("sce211a5a47"), () => _ = RunAsync(() => SaveSceneAsync(name.Text)), true)));
        create.Children.Add(capture);
        PageContent.Children.Add(Ui.Card(create));
        PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Text(L.T("scene.importHelp"), 12), Ui.Button(L.T("scene.import"), () => _ = RunAsync(ImportSceneAsync)))));
        if (sceneDraft != null) PageContent.Children.Add(Ui.Card(CreateSceneDraftEditor(sceneDraft)));
        if (undoScene != null) PageContent.Children.Add(Ui.Card(Ui.Row(Ui.Text(L.T("scene.previous")), Ui.Button(UndoLabel, () => _ = RunAsync(async () => { if (undoScene != null) await ApplySceneAsync(undoScene, true); })))));
        if (!string.IsNullOrEmpty(sceneReport)) PageContent.Children.Add(Ui.Card(new Expander { Header = L.T("scene.details"), Content = Ui.Text(sceneReport, 12) }));
        var search = new TextBox { Text = sceneSearch, MinWidth = 180 };
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("scene.search"));
        var clear = Ui.Button(L.T("scene.clearSearch"), () => { search.Clear(); search.Focus(); });
        var count = Ui.Text("", 11);
        count.Margin = new Thickness(0, 5, 0, 0);
        var searchPanel = new StackPanel();
        searchPanel.Children.Add(new Label { Content = L.T("scene.search"), Target = search });
        searchPanel.Children.Add(Ui.Row(search, clear));
        searchPanel.Children.Add(count);
        var searchCard = Ui.Card(searchPanel);
        searchCard.Visibility = Preferences.Presets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageContent.Children.Add(searchCard);
        var emptyMessage = Ui.Text(L.T("scene.noSearchResults"));
        emptyMessage.TextWrapping = TextWrapping.Wrap;
        var noMatches = Ui.Card(emptyMessage);
        PageContent.Children.Add(noMatches);
        var cards = new List<(Preset Scene, Border Card, StackPanel Order)>();
        if (Preferences.Presets.Count == 0) PageContent.Children.Add(Ui.Card(Ui.Labels(L.T("s502f445e9e"), L.T("scene.empty"))));
        foreach (var scene in Preferences.Presets.ToArray())
        {
            var card = new StackPanel();
            card.Children.Add(Ui.Row(Ui.Labels(scene.Name, L.F("sb91b8e2562", scene.Displays.Count)), Ui.Button(L.T(SceneApplyPreview.Enabled ? "scene.previewApply" : "scene.apply"), () => _ = RunAsync(async () => await ApplySceneAsync(scene, preview: true)), true)));
            if (!string.IsNullOrWhiteSpace(scene.Note))
            {
                var note = Ui.Text(scene.Note, 12, "#617681");
                note.TextWrapping = TextWrapping.Wrap;
                note.MaxHeight = 54;
                note.TextTrimming = TextTrimming.CharacterEllipsis;
                note.ToolTip = scene.Note;
                note.Margin = new Thickness(0, 8, 0, 0);
                System.Windows.Automation.AutomationProperties.SetAutomationId(note, "SceneNotePreview");
                card.Children.Add(note);
            }
            var order = CreateSceneOrderControls(scene);
            card.Children.Add(order);
            var details = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (GammaService.Enabled)
                foreach (var display in scene.Displays)
                    details.Children.Add(Ui.Labels(display.Name, L.T(display.GammaRamp == null ? "gamma.absent" : "gamma.saved")));
            details.Children.Add(CreateSceneNoteEditor(scene));
            details.Children.Add(Ui.Line());
            var rename = new TextBox { Text = scene.Name, MaxLength = SceneService.MaxNameLength };
            System.Windows.Automation.AutomationProperties.SetName(rename, L.T("s2c09021a16"));
            details.Children.Add(Ui.Row(rename, Ui.Button(L.T("scene.rename"), () => RenameScene(scene, rename.Text))));
            details.Children.Add(Ui.Button(L.T("scene.update"), () => _ = RunAsync(() => SaveSceneAsync(rename.Text, scene))));
            details.Children.Add(Ui.Button(L.T("scene.export"), () => _ = RunAsync(() => ExportSceneAsync(scene))));
            if (CreateSceneComparison(scene) is UIElement comparison) details.Children.Add(comparison);
            details.Children.Add(Ui.Line());
            details.Children.Add(CreateSceneDuplicateEditor(scene));
            details.Children.Add(Ui.Line());
            details.Children.Add(CreateSceneDisplayEditor(scene));
            details.Children.Add(Ui.Line());
            details.Children.Add(Ui.Setting(L.T("scene.onConnect"), L.T("scene.onConnectHelp"), Toggle(scene.OnConnect, v =>
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
            var container = Ui.Card(card);
            cards.Add((scene, container, order));
            PageContent.Children.Add(container);
        }
        void Filter()
        {
            sceneSearch = search.Text;
            bool filtering = !string.IsNullOrWhiteSpace(sceneSearch);
            int matches = 0;
            foreach (var entry in cards)
            {
                bool match = SceneService.MatchesName(entry.Scene, sceneSearch);
                entry.Card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                // Reordering across hidden neighbors would be surprising.
                entry.Order.Visibility = filtering ? Visibility.Collapsed : Visibility.Visible;
                if (match) matches++;
            }
            count.Text = L.F("scene.searchCount", matches, cards.Count);
            clear.IsEnabled = sceneSearch.Length > 0;
            noMatches.Visibility = matches == 0 && cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        // Change visibility in place so typing retains focus and unsaved card edits.
        search.TextChanged += (_, _) => Filter();
        Filter();
    }

    private Task ImportSceneAsync()
    {
        if (sceneBusy || modePending || scanning || Service.IsWriting) throw new InvalidOperationException(L.T("s92f637408f"));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        using var stream = File.OpenRead(dialog.FileName);
        var scene = SceneFile.Import(stream, Preferences.Presets);
        Preferences.Presets.Add(scene);
        Save(); Render(); quick?.Render(); Status(L.T("scene.imported"));
        return Task.CompletedTask;
    }

    private Task ExportSceneAsync(Preset scene)
    {
        string json = SceneFile.Export(scene);
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "lumadesk-scene.json", DefaultExt = ".json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, json);
        return Task.CompletedTask;
    }
}
