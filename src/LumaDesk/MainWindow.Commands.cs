// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

public partial class MainWindow
{
    private CommandService? commands;
    private void StartCommands()
    {
        if (testing) return;
        commands = new CommandService(command => Dispatcher.InvokeAsync(() => ExecuteCommand(command)).Task.Unwrap());
    }
    internal async Task<ControlReply> ExecuteCommand(ControlCommand command)
    {
        if (command.Action == "list") return new(true, Displays);
        if (sceneBusy || modePending || scanning || restoringConnection) return new(false, Error: L.T("s92f637408f"));
        if (command.Action == "preset")
        {
            var preset = Preferences.Presets.FirstOrDefault(p => p.Name.Equals(command.Target, StringComparison.OrdinalIgnoreCase) || p.Id == command.Target);
            if (preset == null) return new(false, Error: L.T("command.presetMissing"));
            var result = await ApplySceneAsync(preset);
            return new(result?.Issues.Count == 0, result, result?.Issues.Count > 0 ? string.Join("\n", result.Issues) : null);
        }
        if (command.Action == "brightness" && command.Value is >= 0 and <= 100)
        {
            var candidates = Displays.Where(d => d.Id.Equals(command.Target, StringComparison.OrdinalIgnoreCase) || d.Name.Equals(command.Target, StringComparison.OrdinalIgnoreCase) || d.Number.ToString() == command.Target).ToArray();
            if (candidates.Length != 1) return new(false, Error: L.T("command.displayMissing"));
            await ChangeCombinedBrightness(candidates[0], command.Value.Value); quick?.Render();
            return new(true, new { Display = candidates[0].Id, Brightness = GetCombinedBrightness(candidates[0]) });
        }
        return new(false, Error: "Unknown command.");
    }
}
