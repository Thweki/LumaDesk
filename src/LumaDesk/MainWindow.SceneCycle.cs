// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace LumaDesk;

public partial class MainWindow
{
    private readonly SceneCycle sceneCycle = new();
    private bool CanCycleScene => !sceneBusy && !modePending && !scanning && !restoringConnection && !Service.IsWriting;

    internal void AddSceneCycleTrayItems(Forms.ContextMenuStrip menu, Func<Preset, Task>? apply = null)
    {
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(-1, "scene.previousPreset");
        Add(1, "scene.nextPreset");
        void Add(int direction, string key)
        {
            var item = new Forms.ToolStripMenuItem();
            void Refresh()
            {
                var target = sceneCycle.Select(Preferences.Presets, direction);
                string label = L.T(key) + (target == null ? "" : ": " + target.Name);
                item.Text = label.Replace("&", "&&");
                item.AccessibleName = label;
                item.Enabled = target != null && CanCycleScene;
            }
            menu.Opening += (_, _) => Dispatcher.Invoke(Refresh);
            item.Click += (_, _) => Dispatcher.Invoke(() => _ = RunAsync(async () =>
            {
                // Resolve again: a saved preset or busy state may have changed since opening.
                if (!CanCycleScene) return;
                var target = sceneCycle.Select(Preferences.Presets, direction);
                if (target == null) return;
                if (apply != null) await apply(target);
                else await ApplySceneAsync(target);
            }));
            menu.Items.Add(item);
            Refresh();
        }
    }
}
