# Overscan and underscan (experimental)

Some external monitors and TVs crop the desktop edges or leave a border. LumaDesk can now select the monitor's scan mode when Windows exposes it through DDC/CI. This experiment defaults **OFF**.

## Use

1. Exit LumaDesk, including its tray process.
2. Launch it with the process environment variable `LUMADESK_EXPERIMENTAL_OVERSCAN=1`. For example, from PowerShell in the app directory:

   ```powershell
   $env:LUMADESK_EXPERIMENTAL_OVERSCAN = '1'
   .\LumaDesk.exe
   ```

3. Open the full settings window, select a display, and expand **Overscan / underscan (experimental)**.
4. Choose an advertised mode and click **Apply scan mode**. Selecting an item alone changes nothing. **Normal** removes overscan, **Underscan** can expose clipped edges, and **Overscan** may crop the image. **Widescreen** is also offered if advertised by the monitor.
5. The app confirms success only after two consecutive matching readbacks. After an error, click **Refresh** to discover the actual state before trying another change.

To disable the experiment, exit LumaDesk, remove the environment variable, and restart. Disabling the feature hides the control; it does not restore a monitor setting. Select the previous mode before disabling if you want to restore it, or use the monitor's own menu.

## Compatibility and scope

- Uses the discrete MCCS VCP `0xDA` scan-mode control via the Windows monitor configuration API: `00` normal, `01` underscan, `02` overscan, `03` widescreen. See the [MCCS values documented by ddcutil](https://www.ddcutil.com/vcpinfo_output/) and Microsoft's [SetVCPFeature documentation](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-setvcpfeature).
- Only recognized modes explicitly listed in a well-formed capability response are offered. The current mode must also be readable and advertised. A missing capability list, unknown current value, or unsupported connection leaves Apply disabled with an explanation.
- There is no universal Windows percentage-based overscan adjustment. This feature changes the monitor's scan mode, not Windows DPI scaling, resolution, placement, contrast, or a GPU vendor's custom scaling percentage. Monitors exposing only vendor-specific controls are unsupported.
- Discovery happens only when the section is expanded or refreshed. No new scan-mode probes run at startup. Writes reacquire and validate the display identity, reread capabilities, and use the existing per-monitor DDC lock. Readback retries are bounded; writes are never retried automatically.
- Changes affect only the selected monitor. Monitor firmware may persist them. They are not saved in LumaDesk preferences or presets, and there is no automatic rollback.

## Validation

Added logic tests for default-off gating, capability parsing, discrete zero-valued writes, unsupported targets, changed capabilities, failed writes, disconnects, and bounded verification. Added detached WPF fixtures for default-off UI/service behavior, explicit Apply, duplicate-action prevention, unsupported hardware, and recovery after errors. Both existing UI entry points run the fixture.

On Windows with .NET 8 and PowerShell:

```powershell
dotnet run --project tests/LumaDesk.Tests -c Release
dotnet run --project tests/LumaDesk.UiTests -c Release -- --fixtures artifacts/wpf-fixtures
.\test.ps1 -Headless
```

Local whitespace, localization JSON/key/format-argument, project XML/source-reference, and test-registration checks passed. The C# tests and WPF fixtures were **not executed** in the implementation environment because `dotnet` and `pwsh` are unavailable. Hardware compatibility and visible behavior still require a Windows monitor with advertised VCP `DA` support: verify the initial mode, apply another mode, refresh, restore the initial mode, and check an unsupported monitor.

Suggested PR title: **experimental: add capability-gated overscan and underscan controls**.
