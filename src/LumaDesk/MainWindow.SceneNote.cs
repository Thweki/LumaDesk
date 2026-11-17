// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    private StackPanel CreateSceneNoteEditor(Preset scene)
    {
        var note = new TextBox
        {
            Text = scene.Note ?? "", MaxLength = SceneService.NoteMaxLength,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 64, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(note, scene.Name + ": " + L.T("scene.note"));
        AutomationProperties.SetAutomationId(note, "SceneNoteEditor");
        var panel = new StackPanel();
        panel.Children.Add(Ui.Labels(L.T("scene.note"), L.F("scene.noteHelp", SceneService.NoteMaxLength)));
        panel.Children.Add(Ui.Row(note, Ui.Button(L.T("scene.saveNote"), () => SaveSceneNote(scene, note.Text))));
        return panel;
    }

    private void SaveSceneNote(Preset scene, string note)
    {
        if (sceneBusy || !Preferences.Presets.Contains(scene)) return;
        string? previous = scene.Note;
        try { SceneService.SetNote(scene, note); }
        catch (ArgumentException ex) { Status(ex.Message, true); return; }
        try { Storage.Save(Preferences); }
        catch (Exception ex)
        {
            scene.Note = previous;
            Status(L.T("s888403f2f3") + ex.Message, true);
            return;
        }
        Render(); Status(L.T("sa0d6e1ed82"));
    }
}
