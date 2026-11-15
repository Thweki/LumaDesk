# Experimental brightness easing

Manual brightness changes can ease over approximately 240 ms, making large changes less abrupt. This covers the brightness slider, five-point buttons, keyboard brightness shortcuts, linked groups and explicit multi-display selection, including software-only dimming. The experiment is **OFF by default** and does not add or change a saved preference.

Exit any running LumaDesk instance, then launch from PowerShell with a process-scoped flag:

```powershell
$env:LUMADESK_EXPERIMENTAL_TRANSITIONS = '1'
.\LumaDesk.exe
Remove-Item Env:\LUMADESK_EXPERIMENTAL_TRANSITIONS
```

Only the exact value `1` enables the experiment. The flag is read at application startup. To disable it, exit and relaunch without the flag. Easing also requires Windows' **Animation effects** preference to be enabled.

The transition uses up to four eased samples, serialized with the existing per-display brightness lock. It preserves the requested hardware/dimming endpoint and existing group limits. Slow monitor writes skip intermediate samples instead of building a backlog; a slow DDC call can still make the total operation exceed 240 ms. Rapid slider requests continue to coalesce to the newest pending value after the current transition. Displays without backlight control ease only their software dimming.

Preset application, layout changes, reconnect restoration and the remove-dimming action keep their immediate behavior. Failed writes stop the transition and use the existing error reporting. Completed samples remain the known brightness state; shutdown cancels remaining samples.

## Validation

`BrightnessTransitionTests` runs as part of `tests/LumaDesk.Tests`. Its injected clock and delay exercise opt-in parsing, the immediate disabled path, exact eased samples, monotonic hardware/software paths, rounded duplicate suppression, slow-write frame skipping, cancellation, error propagation and integration with the latest-value slider queue.

On Windows with the .NET 8 SDK:

```powershell
dotnet run --project tests/LumaDesk.Tests -c Release
.\test.ps1 -Headless
```

Manual checks on a Windows desktop:

1. Without the flag, move brightness between distant values and confirm immediate behavior.
2. Relaunch with the flag, repeat on a DDC display and a software-only display, and cross the low-brightness dimming boundary in both directions.
3. Drag rapidly, adjust a linked group and an explicit selection, and confirm all displays finish at their requested levels within their configured limits.
4. Disable Windows animation effects and confirm changes are immediate even with the flag set.
5. Refresh or disconnect a display during a transition; confirm refresh waits for the write and disconnects use normal error reporting. Quit during a transition and confirm no overlay reappears.

The C# suites and Windows hardware checks were not executed in the implementation environment: `dotnet` and `pwsh` are unavailable. Whitespace and project/source wiring checks were performed locally.

Suggested PR title: `experimental: ease manual brightness changes behind an opt-in flag`.
