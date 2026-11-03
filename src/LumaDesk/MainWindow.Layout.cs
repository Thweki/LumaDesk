// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LumaDesk;

public partial class MainWindow
{
    internal void ShowLayout() { quick?.Close(); new LayoutWindow(this).Show(); }
    internal void ShowVirtualDisplays() { quick?.Close(); new VirtualDisplayWindow(this).Show(); }
    internal async Task ChangeLayoutAsync(IReadOnlyList<ScreenPlacement> placements)
    {
        if (modePending || scanning) throw new InvalidOperationException(L.T("s92f637408f"));
        modePending = true;
        try { var devices = Displays.ToList(); await Task.Run(() => LayoutService.Apply(devices, placements)); }
        finally { modePending = false; await RefreshAsync(); }
    }
    internal async Task ChangeTopologyAsync(bool extend)
    {
        if (modePending || scanning) throw new InvalidOperationException(L.T("s92f637408f"));
        modePending = true;
        try { await Task.Run(() => LayoutService.SetTopology(extend)); }
        finally { modePending = false; await RefreshAsync(); }
    }
}
