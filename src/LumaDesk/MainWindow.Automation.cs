// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LumaDesk;

public partial class MainWindow
{
    private readonly DispatcherTimer automationTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer preferenceTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private ReconnectCoordinator? reconnect;
    private bool restoringConnection => reconnect?.IsRestoring == true;
    private bool resumeBrightness;
    private DateTime lastAutomationTime;
    private Forms.PowerLineStatus lastPower;
    private string lastConnectionScene = "";
    private void CancelReconnect(string id) => reconnect?.Cancel(id);
    private bool testing => System.Windows.Application.Current is App { IntegrationTest: true };

    private void RememberBrightness(DisplayInfo d)
    {
        Preferences.RememberedBrightness[d.Id] = new PresetDisplay { Id = d.Id, Name = d.Name, Brightness = d.Brightness, Dimming = d.Dimming };
        preferenceTimer.Stop(); preferenceTimer.Start();
    }
    private void StartAutomation()
    {
        reconnect ??= new(new ReconnectBackend(this), Displays.Select(d => d.Id));
        lastAutomationTime = DateTime.Now; lastPower = Forms.SystemInformation.PowerStatus.PowerLineStatus;
        preferenceTimer.Tick += (_, _) => { preferenceTimer.Stop(); Save(); };
        automationTimer.Tick += async (_, _) => await TickAutomationAsync();
        if (!testing) { UpdateNightDimming(DateTime.Now); automationTimer.Start(); }
        StartIdleDimming();
    }
    private void StopAutomation()
    {
        automationTimer.Stop(); preferenceTimer.Stop();
        idleDimmingTimer.Stop(); ResetIdleDimming();
        reconnect?.CancelAll();
    }

    private async Task RestoreAfterConnectionAsync()
    {
        if (modePending || sceneBusy || scanning || restoringConnection) { hotplugTimer.Start(); return; }
        reconnect ??= new(new ReconnectBackend(this), Displays.Select(d => d.Id));
        bool resume = resumeBrightness; resumeBrightness = false;
        try { await reconnect.RestoreAsync(resume, automate: !testing); }
        catch (Exception ex) { Status(ex.Message, true); }
    }

    private async Task ConnectionRestoredAsync(bool resume, bool allowScene)
    {
        var scene = Preferences.Presets.FirstOrDefault(p => p.OnConnect && SceneService.SameScreens(p, Displays));
        string connection = scene == null ? "" : scene.Id + ":" + string.Join("|", Displays.Select(d => d.Id).OrderBy(id => id));
        if (scene != null && (resume || lastConnectionScene != connection) && !sceneBusy && !modePending && allowScene)
        {
            lastConnectionScene = connection;
            await ApplySceneAsync(scene);
        }
        else if (scene == null) lastConnectionScene = "";
        RefreshView(); quick?.Render();
    }

    private void CompleteReconnectBrightness(DisplayInfo display, double dimming)
    {
        if (display.Dimming != dimming) SetDimming(display, dimming);
        RememberBrightness(display);
        BrightnessChanged?.Invoke(display);
    }

    private sealed class ReconnectBackend : IReconnectBackend
    {
        private readonly MainWindow window;
        internal ReconnectBackend(MainWindow window) { this.window = window; }
        public IReadOnlyList<DisplayInfo> Displays => window.Displays;
        public Preferences Preferences => window.Preferences;
        public bool IsBlocked => window.sceneBusy || window.modePending || window.scanning;
        public Task RefreshAsync() => window.RefreshAsync();
        public async Task<IDisposable> AcquireBrightnessAsync(string id, CancellationToken cancellation)
        {
            if (!window.brightnessGates.TryGetValue(id, out var gate)) window.brightnessGates[id] = gate = new(1, 1);
            await gate.WaitAsync(cancellation);
            return new BrightnessLease(gate);
        }
        private sealed class BrightnessLease : IDisposable
        {
            private readonly SemaphoreSlim gate;
            internal BrightnessLease(SemaphoreSlim gate) { this.gate = gate; }
            public void Dispose() => gate.Release();
        }
        public Task WriteBrightnessAsync(DisplayInfo display, int value, CancellationToken cancellation) => window.Service.SetPercentAsync(display, 0x10, value, cancellation);
        public void SetDimming(DisplayInfo display, double value) => window.CompleteReconnectBrightness(display, value);
        public void LogFailure(DisplayInfo display, Exception error) => Storage.Log("Reconnect brightness: " + display.Name + ": " + error.Message);
        public void RestoreFailed(IReadOnlyList<PresetDisplay> targets) => window.Status(L.T("automation.restoreFailed") + string.Join("、", targets.Select(d => d.Name)), true);
        public Task OnRestoredAsync(bool resume, bool allowScene) => window.ConnectionRestoredAsync(resume, allowScene);
    }

    private async Task TickAutomationAsync()
    {
        if (sceneBusy || modePending || scanning || restoringConnection || testing) return;
        DateTime now = DateTime.Now;
        UpdateNightDimming(now);
        var power = Forms.SystemInformation.PowerStatus.PowerLineStatus;
        var scenes = Preferences.Presets.Where(p =>
            AutomationRules.CrossedSchedule(p, lastAutomationTime, now) ||
            (power != lastPower && ((p.OnPower == "ac" && power == Forms.PowerLineStatus.Online) || (p.OnPower == "battery" && power == Forms.PowerLineStatus.Offline)))).ToArray();
        lastAutomationTime = now; lastPower = power;
        // Stable list order breaks ties; never run conflicting scenes back to back.
        var scene = scenes.FirstOrDefault(p => SceneService.SameScreens(p, Displays));
        if (scene != null) await RunAsync(async () => await ApplySceneAsync(scene));
    }

    internal UIElement CreateSceneSchedule(Preset scene)
    {
        var stack = new StackPanel();
        var days = new WrapPanel { IsEnabled = scene.ExperimentalWeeklySchedule && scene.AtTime != null };
        var time = new TextBox { Text = scene.AtTime ?? "21:00", Width = 90, IsEnabled = scene.AtTime != null, MaxLength = 5 };
        var enabled = Toggle(scene.AtTime != null, value =>
        {
            time.IsEnabled = value; scene.AtTime = value ? (AutomationRules.ValidTime(time.Text) ? time.Text : "21:00") : null;
            time.Text = scene.AtTime ?? "21:00";
            days.IsEnabled = value && scene.ExperimentalWeeklySchedule; Save();
        });
        time.LostKeyboardFocus += (_, _) =>
        {
            if (!time.IsEnabled) return;
            if (AutomationRules.ValidTime(time.Text)) { scene.AtTime = time.Text; Save(); }
            else { time.Text = scene.AtTime ?? "21:00"; Status(L.T("automation.timeFormat"), true); }
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(time); enabled.Margin = new Thickness(12, 0, 0, 0); right.Children.Add(enabled);
        System.Windows.Automation.AutomationProperties.SetAutomationId(enabled, "schedule-enabled");
        var timeLabel = Ui.Text(L.T(scene.ExperimentalWeeklySchedule ? "automation.weeklyTime" : "automation.daily"));
        stack.Children.Add(Ui.Line()); stack.Children.Add(Ui.Row(timeLabel, right));
        var weekly = Toggle(scene.ExperimentalWeeklySchedule, value =>
        {
            scene.ExperimentalWeeklySchedule = value;
            days.IsEnabled = value && scene.AtTime != null;
            timeLabel.Text = L.T(value ? "automation.weeklyTime" : "automation.daily");
            Save();
        });
        System.Windows.Automation.AutomationProperties.SetAutomationId(weekly, "schedule-weekly");
        System.Windows.Automation.AutomationProperties.SetName(weekly, L.T("automation.weekly"));
        stack.Children.Add(Ui.Line());
        var weeklyLabels = Ui.Labels(L.T("automation.weekly"), L.T("automation.weeklyHelp"));
        foreach (var label in weeklyLabels.Children.OfType<TextBlock>()) label.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(Ui.Row(weeklyLabels, weekly));
        // Keep the visible order Monday–Sunday, independent of DayOfWeek's Sunday-first values.
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var choice = Toggle(scene.ScheduleDays?.Contains(day) == true, value =>
            {
                scene.ScheduleDays ??= new();
                scene.ScheduleDays.RemoveAll(d => d == day);
                if (value) scene.ScheduleDays.Add(day);
                Save();
            });
            choice.Content = CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(day);
            choice.Margin = new Thickness(0, 8, 12, 0);
            System.Windows.Automation.AutomationProperties.SetAutomationId(choice, "schedule-day-" + day);
            days.Children.Add(choice);
        }
        stack.Children.Add(days);
        var power = new ComboBox { ItemsSource = new[] { L.T("automation.never"), L.T("automation.ac"), L.T("automation.battery") }, SelectedIndex = scene.OnPower == "ac" ? 1 : scene.OnPower == "battery" ? 2 : 0 };
        power.SelectionChanged += (_, _) => { scene.OnPower = power.SelectedIndex == 1 ? "ac" : power.SelectedIndex == 2 ? "battery" : null; Save(); };
        stack.Children.Add(Ui.Line()); stack.Children.Add(Ui.Row(Ui.Text(L.T("automation.power")), power));
        return stack;
    }
}

internal static class AutomationRules
{
    internal static bool ValidTime(string? value) => TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out _);
    internal static (Preset Preset, DateTime At)? NextScheduledPreset(IEnumerable<Preset> presets, IReadOnlyList<DisplayInfo> displays, DateTime now)
    {
        (Preset Preset, DateTime At)? next = null;
        foreach (var preset in presets)
        {
            if (!TimeSpan.TryParseExact(preset.AtTime, @"hh\:mm", CultureInfo.InvariantCulture, out var time) ||
                !SceneService.SameScreens(preset, displays)) continue;
            DateTime at = now.Date + time;
            if (at <= now) at = at.AddDays(1);
            // As in TickAutomationAsync, the first preset in list order wins ties.
            if (next == null || at < next.Value.At) next = (preset, at);
        }
        return next;
    }

    internal static bool CrossedTime(string? value, DateTime previous, DateTime now)
        => CrossedScheduleTime(value, previous, now, null);

    internal static bool CrossedSchedule(Preset scene, DateTime previous, DateTime now)
        => !scene.ExperimentalWeeklySchedule ? CrossedTime(scene.AtTime, previous, now)
            : CrossedScheduleTime(scene.AtTime, previous, now, scene.ScheduleDays ?? new());

    private static bool CrossedScheduleTime(string? value, DateTime previous, DateTime now, IReadOnlyCollection<DayOfWeek>? days)
    {
        if (!TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time) || now <= previous) return false;
        DateTime candidate = now.Date + time;
        if (candidate > now) candidate = candidate.AddDays(-1);
        // Inspect only the latest daily occurrence. Never replay an older selected weekday
        // after a long sleep, or use the wake-up day's weekday for a midnight crossing.
        return candidate > previous && now - candidate < TimeSpan.FromDays(1) &&
            (days == null || days.Contains(candidate.DayOfWeek));
    }
}
