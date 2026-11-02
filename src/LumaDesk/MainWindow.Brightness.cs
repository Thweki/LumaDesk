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
        if (failures.Count > 0) throw new InvalidOperationException("组合亮度未能更新：" + string.Join("、", failures) + "。请重新检测读取实际状态。");
        Status($"组合亮度 {value}% · " + (targets.Count == 1 ? display.Name : $"{targets.Count} 台屏幕"));
    }
}
