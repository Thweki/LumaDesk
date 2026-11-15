# Compact tray display picker (experimental)

The compact picker makes large display setups easier to navigate: it replaces
the spatial map with two columns of named buttons, each showing its current
brightness. Six displays fit in 108 DIPs of height; additional displays scroll
inside the picker without pushing the adjustment controls out of the panel.
Long names are shortened visually, with the full name available in the tooltip
and to accessibility tools.

This experiment is **off by default**. Quit LumaDesk, then launch it from a
PowerShell session with the process environment flag enabled:

```powershell
$env:LUMADESK_EXPERIMENTAL_TRAY_COMPACT = '1'
.\LumaDesk.exe
```

Open the tray panel and choose **Compact** beside **Link brightness**. Choose
**Map** to return to the spatial view. The selected display, expanded controls,
and linked brightness selection survive switching layouts. Left/Right move
between buttons; Up/Down move by two buttons. In linked brightness mode, arrows
move focus and Space selects or deselects a display. Mouse wheel scrolling and
Tab also reach displays beyond the visible rows.

The layout choice lasts until the panel closes. No preference or display setting
is changed by switching layouts. A pending adjustment blocks layout switching
until it finishes, just as it blocks selecting a different display.

To disable the experiment, quit LumaDesk, remove the process environment flag,
and launch again. Only the exact flag value `1` enables the switch.

## Validation

`QuickCompactLayoutTests.cs` runs in both the WPF fixture runner and the full UI
suite. It covers flag gating, bounded scrolling, full accessible names, stable
selection, write-in-progress protection, multiple-display brightness targets,
layout switching, session reset, and non-overlapping tile geometry. The full UI
run also checks keyboard focus and row navigation and captures a screenshot.

```powershell
dotnet run --project tests/LumaDesk.UiTests -c Release -- --fixtures
.\test.ps1
```

These tests were not executed in the implementation environment: neither
`dotnet` nor `pwsh` is available. JSON catalog parity, format-token checks, and
`git diff --check` were run locally. Windows UI validation remains required.
