// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace LumaDesk;

public partial class MainWindow
{
    private CancellationTokenSource? updateChecks;
    private PendingUpdate? pendingUpdate;
    private string updateDetail = "";
    private TextBlock? updateDescription;

    private UIElement CreateAutoUpdateOption()
    {
        var toggle = Toggle(Preferences.AutoUpdate, enabled =>
        {
            Preferences.AutoUpdate = enabled;
            try { Storage.Save(Preferences); }
            catch (Exception ex)
            {
                Preferences.AutoUpdate = !enabled;
                Status(L.T("s888403f2f3") + ex.Message, true);
                Render();
                return;
            }
            if (enabled) StartAutoUpdates();
            else StopUpdateChecks();
            Render();
        });
        AutomationProperties.SetName(toggle, L.T("updates.title"));
        var labels = Ui.Labels(L.T("updates.title"), updateDetail.Length > 0 ? updateDetail : L.T("updates.help"));
        updateDescription = (TextBlock)labels.Children[1];
        updateDescription.TextWrapping = TextWrapping.Wrap;
        return Ui.Row(labels, toggle);
    }

    internal void StartAutoUpdates()
    {
        if (!Preferences.AutoUpdate || testing || updateChecks != null) return;
        updateChecks = new();
        _ = CheckForUpdatesAsync(updateChecks);
    }

    private async Task CheckForUpdatesAsync(CancellationTokenSource checks)
    {
        var token = checks.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var update = await Task.Run(() => ReleaseUpdates.DownloadAsync(token), token);
                    if (token.IsCancellationRequested) { update?.Discard(); return; }
                    pendingUpdate = update;
                    updateDetail = update == null ? L.F("updates.current", AppBrand.Version) : L.F("updates.ready", update.Version);
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) return;
                    Storage.Log("Update check: " + ex.Message);
                    updateDetail = L.T("updates.failed");
                }
                if (page == "application" && updateDescription != null) updateDescription.Text = updateDetail;
                if (pendingUpdate != null) return;
                await Task.Delay(TimeSpan.FromDays(1), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(updateChecks, checks)) updateChecks = null;
            checks.Dispose();
        }
    }

    private void StopUpdateChecks()
    {
        updateChecks?.Cancel();
        updateChecks = null;
        pendingUpdate?.Discard();
        pendingUpdate = null;
        updateDetail = "";
    }

    private void FinishAutoUpdates()
    {
        if (Preferences.AutoUpdate && pendingUpdate != null)
        {
            try { pendingUpdate.InstallAfterExit(); pendingUpdate = null; }
            catch (Exception ex) { Storage.Log("Update handoff: " + ex.Message); }
        }
        StopUpdateChecks();
    }
}
