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
    }
    private void StopAutomation()
    {
        automationTimer.Stop(); preferenceTimer.Stop();
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
            AutomationRules.CrossedTime(p.AtTime, lastAutomationTime, now) ||
            (power != lastPower && ((p.OnPower == "ac" && power == Forms.PowerLineStatus.Online) || (p.OnPower == "battery" && power == Forms.PowerLineStatus.Offline)))).ToArray();
        lastAutomationTime = now; lastPower = power;
        // Stable list order breaks ties; never run conflicting scenes back to back.
        var scene = scenes.FirstOrDefault(p => SceneService.SameScreens(p, Displays));
        if (scene != null) await RunAsync(async () => await ApplySceneAsync(scene));
    }

    private UIElement CreateSceneSchedule(Preset scene)
    {
        var stack = new StackPanel();
        var time = new TextBox { Text = scene.AtTime ?? "21:00", Width = 90, IsEnabled = scene.AtTime != null, MaxLength = 5 };
        var enabled = Toggle(scene.AtTime != null, value =>
        {
            time.IsEnabled = value; scene.AtTime = value ? (AutomationRules.ValidTime(time.Text) ? time.Text : "21:00") : null;
            time.Text = scene.AtTime ?? "21:00"; Save();
        });
        time.LostKeyboardFocus += (_, _) =>
        {
            if (!time.IsEnabled) return;
            if (AutomationRules.ValidTime(time.Text)) { scene.AtTime = time.Text; Save(); }
            else { time.Text = scene.AtTime ?? "21:00"; Status(L.T("automation.timeFormat"), true); }
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(time); enabled.Margin = new Thickness(12, 0, 0, 0); right.Children.Add(enabled);
        stack.Children.Add(Ui.Line()); stack.Children.Add(Ui.Row(Ui.Text(L.T("automation.daily")), right));
        var power = new ComboBox { ItemsSource = new[] { L.T("automation.never"), L.T("automation.ac"), L.T("automation.battery") }, SelectedIndex = scene.OnPower == "ac" ? 1 : scene.OnPower == "battery" ? 2 : 0 };
        power.SelectionChanged += (_, _) => { scene.OnPower = power.SelectedIndex == 1 ? "ac" : power.SelectedIndex == 2 ? "battery" : null; Save(); };
        stack.Children.Add(Ui.Line()); stack.Children.Add(Ui.Row(Ui.Text(L.T("automation.power")), power));
        return stack;
    }
}

internal static class AutomationRules
{
    internal static bool ValidTime(string? value) => TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out _);
    internal static bool CrossedTime(string? value, DateTime previous, DateTime now)
    {
        if (!TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time) || now <= previous) return false;
        DateTime candidate = now.Date + time;
        if (candidate > now) candidate = candidate.AddDays(-1);
        return candidate > previous && now - candidate < TimeSpan.FromDays(1);
    }
}
