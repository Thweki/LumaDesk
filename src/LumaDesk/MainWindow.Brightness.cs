// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

public partial class MainWindow
{
    internal int GetCombinedBrightness(DisplayInfo display) => BrightnessRules.Combine(display.Brightness, display.Dimming);
    internal async Task ChangeCombinedBrightness(DisplayInfo display, int value)
    {
        var targets = Preferences.SyncBrightness ? Displays.ToList() : new List<DisplayInfo> { display };
        var failures = new List<string>();
        foreach (var target in targets)
        {
            int previousDimming = target.Dimming;
            var level = BrightnessRules.Split(value, target.Brightness.HasValue);
            try
            {
                if (level.Dimming > previousDimming) SetDimming(target, level.Dimming);
                if (level.Hardware.HasValue && target.Brightness != level.Hardware)
                {
                    await Service.SetPercentAsync(target, 0x10, level.Hardware.Value);
                    target.Brightness = level.Hardware;
                }
                SetDimming(target, level.Dimming);
            }
            catch (Exception ex)
            {
                try { SetDimming(target, previousDimming); } catch (Exception restore) { Storage.Log(restore.Message); }
                failures.Add(target.Name); Storage.Log(ex.ToString());
            }
        }
        if (failures.Count > 0) throw new InvalidOperationException(L.T("s0ed98f4c87") + string.Join("、", failures) + L.T("s335ece13e6"));
        Status(L.F("sb85c114a54", value) + (targets.Count == 1 ? display.Name : L.F("s57a83fca96", targets.Count)));
    }
}
