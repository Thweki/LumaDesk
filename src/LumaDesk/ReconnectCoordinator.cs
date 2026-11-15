// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDesk;

internal interface IReconnectBackend
{
    IReadOnlyList<DisplayInfo> Displays { get; }
    Preferences Preferences { get; }
    bool IsBlocked { get; }
    Task RefreshAsync();
    Task<IDisposable> AcquireBrightnessAsync(string id, CancellationToken cancellation);
    Task WriteBrightnessAsync(DisplayInfo display, int value, CancellationToken cancellation);
    void SetDimming(DisplayInfo display, double value);
    void LogFailure(DisplayInfo display, Exception error);
    void RestoreFailed(IReadOnlyList<PresetDisplay> targets);
    Task OnRestoredAsync(bool resume, bool allowScene);
}

// Used on one UI synchronization context. Ownership spans scans, backoff, writes
// and the connection-scene decision; a refresh must never reset cancellation.
internal sealed class ReconnectCoordinator
{
    private readonly IReconnectBackend backend;
    private readonly Func<TimeSpan, Task> delay;
    private HashSet<string> connected;
    private readonly Dictionary<string, CancellationTokenSource> writes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> overridden = new(StringComparer.OrdinalIgnoreCase);
    private bool stopped, manualOverride;
    internal bool IsRestoring { get; private set; }

    internal ReconnectCoordinator(IReconnectBackend backend, IEnumerable<string> connected, Func<TimeSpan, Task>? delay = null)
    {
        this.backend = backend;
        this.connected = new(connected, StringComparer.OrdinalIgnoreCase);
        this.delay = delay ?? (duration => Task.Delay(duration));
    }

    internal void Cancel(string id)
    {
        if (!IsRestoring) return;
        manualOverride = true;
        overridden.Add(id);
        if (writes.TryGetValue(id, out var source)) source.Cancel();
    }

    internal void CancelAll()
    {
        if (!IsRestoring) return;
        stopped = true;
        foreach (var source in writes.Values) source.Cancel();
    }

    private bool Superseded(string id, PresetDisplay saved)
    {
        if (overridden.Contains(id) || !ReferenceEquals(backend.Preferences.RememberedBrightness.GetValueOrDefault(id), saved))
        {
            Cancel(id);
            return true;
        }
        return false;
    }

    internal async Task RestoreAsync(bool resume, bool automate = true)
    {
        // A second notification cannot steal tokens or release the first run.
        if (IsRestoring || backend.IsBlocked) return;
        IsRestoring = true;
        stopped = manualOverride = false;
        try
        {
            // Capture before yielding: manual changes during the first scan also win.
            var remembered = new Dictionary<string, PresetDisplay>(backend.Preferences.RememberedBrightness, StringComparer.OrdinalIgnoreCase);
            await backend.RefreshAsync();
            if (stopped || backend.IsBlocked) return;
            var pending = new Dictionary<string, PresetDisplay>(StringComparer.OrdinalIgnoreCase);
            foreach (var display in backend.Displays.Where(d => resume || !connected.Contains(d.Id)))
                if (backend.Preferences.RestoreBrightness && remembered.TryGetValue(display.Id, out var saved))
                    pending[display.Id] = saved;
            bool changed = !connected.SetEquals(backend.Displays.Select(d => d.Id));
            connected = new(backend.Displays.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            if ((!changed && !resume) || !automate) return;
            foreach (var id in pending.Keys) writes[id] = new();

            for (int attempt = 0; pending.Count > 0 && attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    await delay(TimeSpan.FromSeconds(attempt));
                    if (stopped || backend.IsBlocked) return;
                    await backend.RefreshAsync();
                    if (stopped || backend.IsBlocked) return;
                }
                foreach (var pair in pending.ToArray())
                {
                    if (stopped || backend.IsBlocked) return;
                    var cancellation = writes[pair.Key].Token;
                    if (Superseded(pair.Key, pair.Value) || cancellation.IsCancellationRequested)
                    { pending.Remove(pair.Key); continue; }
                    var display = backend.Displays.FirstOrDefault(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, pair.Key));
                    if (display == null) continue;
                    try
                    {
                        // Share the manual-write gate through model publication and dimming.
                        using var lease = await backend.AcquireBrightnessAsync(pair.Key, cancellation);
                        cancellation.ThrowIfCancellationRequested();
                        if (stopped || backend.IsBlocked) return;
                        if (Superseded(pair.Key, pair.Value)) { pending.Remove(pair.Key); continue; }
                        var current = backend.Displays.FirstOrDefault(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, pair.Key));
                        if (current == null) continue;
                        display = current;
                        var saved = pair.Value;
                        if (saved.Brightness is < 0 or > 100) throw new InvalidOperationException(L.T("group.invalid"));
                        if (saved.Brightness.HasValue)
                        {
                            if (!display.Brightness.HasValue) throw new InvalidOperationException(L.T("scene.unsupported"));
                            if (display.Brightness != saved.Brightness)
                                await backend.WriteBrightnessAsync(display, saved.Brightness.Value, cancellation);
                            // Native writes may finish after cancellation. Never publish stale
                            // brightness or apply saved dimming over the manual replacement.
                            cancellation.ThrowIfCancellationRequested();
                            if (stopped || backend.IsBlocked) return;
                            if (Superseded(pair.Key, saved)) { pending.Remove(pair.Key); continue; }
                            display.Brightness = saved.Brightness;
                        }
                        cancellation.ThrowIfCancellationRequested();
                        backend.SetDimming(display, Math.Clamp(saved.Dimming, 0, BrightnessRules.MaxDimming));
                        pending.Remove(pair.Key);
                    }
                    catch (OperationCanceledException) { pending.Remove(pair.Key); }
                    catch (Exception ex) { backend.LogFailure(display, ex); }
                }
            }
            if (stopped || backend.IsBlocked) return;
            // A manual edit can arrive while another display's final write awaits.
            foreach (var pair in pending.ToArray())
                if (Superseded(pair.Key, pair.Value)) pending.Remove(pair.Key);
            if (pending.Count > 0) backend.RestoreFailed(pending.Values.ToArray());
            await backend.OnRestoredAsync(resume, !manualOverride);
        }
        finally
        {
            foreach (var source in writes.Values) source.Dispose();
            writes.Clear(); overridden.Clear();
            IsRestoring = false;
        }
    }
}
