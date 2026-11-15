# Refresh timing inspector (experimental)

The display mode picker shows integer refresh rates. The inspector adds a read-only
view of the selected display's configured desktop and physical signal rates, with
up to three decimal places, and Windows Dynamic Refresh Rate (DRR) status. This
helps explain a nominal 60 Hz mode that is actually 59.94 Hz, or a desktop rate
that differs from the signal rate while Windows automatically boosts refresh.

This experiment defaults **OFF**. Exit LumaDesk completely, then launch it from a
PowerShell session with:

```powershell
$env:LUMADESK_EXPERIMENTAL_REFRESH_TIMING = '1'
.\LumaDesk.exe
```

In Settings, select a display and expand **Refresh timing (experimental)** below
the resolution/refresh controls. Expand the section or click **Refresh** to take
a new snapshot. No queries run while the section is initially collapsed. Remove
the environment variable and restart LumaDesk to hide the experiment. It does not
add or change saved preferences, presets, or display configuration.

The values are configured timings, not instantaneous FPS or a live refresh-rate
meter. DRR is Windows' automatic refresh boost; it is distinct from gaming
VRR / Adaptive-Sync. This inspector does **not** determine gaming VRR support,
its enabled state, its range, or whether a game is currently using it. It offers
no VRR or DRR toggle. A disabled DRR result does not mean VRR is unsupported.

On Windows 11 with a compatible driver, the reader requests
`QDC_VIRTUAL_REFRESH_RATE_AWARE` and reports `DISPLAYCONFIG_PATH_BOOST_REFRESH_RATE`.
If the OS rejects that query as unsupported, it retries without refresh awareness
and reports DRR as **not reported**, preserving available timing information.
Access-denied and other query failures remain errors. Missing or invalid timings
remain unknown. Mirrored sources are rejected rather than attributing a target's
timing to the wrong monitor. Display identity is checked before and after reading.

API references: Microsoft's [QueryDisplayConfig documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig),
[path flags](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_path_info),
[target mode indexes](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_path_target_info),
and [signal timing layout](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_video_signal_info).

## Validation

`LumaDesk.ColorTests` covers native layout, fractional rates, legacy and virtual
mode indexes, adapter/target matching, unavailable values, disabled gating,
unsupported-query fallback, mirrored sources, and identity changes. The UI tests
cover lazy loading, opt-in visibility, duplicate refresh prevention, unknown DRR,
failure clearing, and retry. Those UI fixtures also run through `--fixtures` with
injected readers and no hardware access.

Run on Windows:

```powershell
dotnet run --project tests/LumaDesk.ColorTests -c Release
dotnet run --project tests/LumaDesk.LocalizationTests -c Release
dotnet run --project tests/LumaDesk.UiTests -c Debug -- --fixtures artifacts/wpf-fixtures
```

The implementation environment has neither `dotnet` nor `pwsh`; these tests and
the WPF application were **not executed** there. JSON resource consistency,
format placeholders, project references, test registration, and diff whitespace
were checked locally. Hardware validation remains necessary on Windows 10,
Windows 11 with DRR enabled/disabled, fractional-rate monitors, mirror mode, and
disconnect/reconnect. Compare configured values with Windows Advanced display.

Suggested PR title: **experimental: inspect per-display refresh timing and DRR status**.
