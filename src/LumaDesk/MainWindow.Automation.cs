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
    private HashSet<string> connectedScreens = new(StringComparer.OrdinalIgnoreCase);
    private bool resumeBrightness, restoringConnection;
    private DateTime lastAutomationTime;
    private Forms.PowerLineStatus lastPower;
    private string lastConnectionScene = "";
    private readonly Dictionary<string, CancellationTokenSource> reconnectWrites = new(StringComparer.OrdinalIgnoreCase);
    private void CancelReconnect(string id) { if (reconnectWrites.TryGetValue(id, out var token)) token.Cancel(); }
    private bool testing => System.Windows.Application.Current is App { IntegrationTest: true };

    private void RememberBrightness(DisplayInfo d)
    {
        Preferences.RememberedBrightness[d.Id] = new PresetDisplay { Id = d.Id, Name = d.Name, Brightness = d.Brightness, Dimming = d.Dimming };
        preferenceTimer.Stop(); preferenceTimer.Start();
    }
    private void StartAutomation()
    {
        connectedScreens = new(Displays.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
        lastAutomationTime = DateTime.Now; lastPower = Forms.SystemInformation.PowerStatus.PowerLineStatus;
        preferenceTimer.Tick += (_, _) => { preferenceTimer.Stop(); Save(); };
        automationTimer.Tick += async (_, _) => await TickAutomationAsync();
        if (!testing) automationTimer.Start();
    }
    private void StopAutomation()
    {
        automationTimer.Stop(); preferenceTimer.Stop();
        foreach (var token in reconnectWrites.Values) token.Cancel();
    }

    private async Task RestoreAfterConnectionAsync()
    {
        if (modePending || sceneBusy || scanning || restoringConnection) { hotplugTimer.Start(); return; }
        restoringConnection = true;
        bool resume = resumeBrightness; resumeBrightness = false;
        var pending = new Dictionary<string, PresetDisplay>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await RefreshAsync();
            foreach (var d in Displays.Where(d => resume || !connectedScreens.Contains(d.Id)))
                if (Preferences.RestoreBrightness && Preferences.RememberedBrightness.TryGetValue(d.Id, out var saved))
                { pending[d.Id] = saved; reconnectWrites[d.Id] = new(); }
            bool changed = !connectedScreens.SetEquals(Displays.Select(d => d.Id));
            connectedScreens = new(Displays.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            if (!changed && !resume) return;
            if (testing) return;
            // Reacquire handles on every attempt; a wake event often precedes DDC readiness.
            for (int attempt = 0; pending.Count > 0 && attempt < 3; attempt++)
            {
                if (attempt > 0) { await Task.Delay(1000 * attempt); if (sceneBusy || modePending) return; await RefreshAsync(); }
                foreach (var pair in pending.ToArray())
                {
                    var cancellation = reconnectWrites[pair.Key].Token;
                    if (cancellation.IsCancellationRequested) { pending.Remove(pair.Key); continue; }
                    var d = Displays.FirstOrDefault(d => d.Id == pair.Key);
                    if (d == null) continue;
                    // A manual adjustment supersedes the saved reconnect target.
                    if (!ReferenceEquals(Preferences.RememberedBrightness.GetValueOrDefault(d.Id), pair.Value)) { pending.Remove(pair.Key); continue; }
                    try
                    {
                        var saved = pair.Value;
                        if (saved.Brightness.HasValue)
                        {
                            if (!d.Brightness.HasValue) throw new InvalidOperationException(L.T("scene.unsupported"));
                            if (d.Brightness != saved.Brightness) await Service.SetPercentAsync(d, 0x10, saved.Brightness.Value, cancellation);
                            cancellation.ThrowIfCancellationRequested(); d.Brightness = saved.Brightness;
                        }
                        cancellation.ThrowIfCancellationRequested(); SetDimming(d, saved.Dimming, restoring: true); pending.Remove(pair.Key);
                    }
                    catch (OperationCanceledException) { pending.Remove(pair.Key); }
                    catch (Exception ex) { Storage.Log("Reconnect brightness: " + d.Name + ": " + ex.Message); }
                }
            }
            if (pending.Count > 0) Status(L.T("automation.restoreFailed") + string.Join("、", pending.Values.Select(d => d.Name)), true);
            var scene = Preferences.Presets.FirstOrDefault(p => p.OnConnect && SceneService.SameScreens(p, Displays));
            string connection = scene == null ? "" : scene.Id + ":" + string.Join("|", connectedScreens.OrderBy(id => id));
            if (scene != null && (resume || lastConnectionScene != connection) && !sceneBusy && !modePending && !reconnectWrites.Values.Any(c => c.IsCancellationRequested))
            {
                lastConnectionScene = connection;
                await ApplySceneAsync(scene);
            }
            else if (scene == null) lastConnectionScene = "";
            quick?.Render();
        }
        catch (Exception ex) { Status(ex.Message, true); }
        finally
        {
            foreach (var token in reconnectWrites.Values) token.Dispose(); reconnectWrites.Clear();
            restoringConnection = false;
        }
    }

    private async Task TickAutomationAsync()
    {
        if (sceneBusy || modePending || scanning || restoringConnection || testing) return;
        DateTime now = DateTime.Now;
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
