# Windows connection labels

In the tray panel, select a display, open **More settings → Input source**, and
look for **Windows connection: HDMI** (or another recognized connection type).
Use **Read again** to refresh it. This read-only feature needs no setting or flag.

The label helps identify this PC's connection even when the monitor cannot report
its current input through DDC/CI. It uses the active Windows display path's
[connector type](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_video_output_technology),
independently of DPI scaling support. Recognized labels are VGA, DVI, HDMI, LVDS,
DisplayPort, eDP, Miracast, and built-in display.

**Current input** still comes exclusively from the monitor's DDC readback. Windows
cannot tell LumaDesk whether the monitor calls a connector “HDMI 1” or “HDMI 2,”
and an adapter or dock may report a different connection type. The OS label never
adds input choices, enables a switch, or confirms a switch. Hover over the label
for this distinction in the app.

Unknown connector types, unavailable targets, ambiguous mirrored paths, and failed
Windows queries leave the label hidden. It is cleared when a switch starts or a
refresh fails; **Read again** can restore it from a fresh query.

## Validation

`InputConnectionTests` in WorkflowTests covers connector names, unknown values,
separation from DDC codes, and preservation of firmware compatibility mappings.
The UI checks run in both UiTests and its desktop-independent `--fixtures` runner,
using injected callbacks for absent DDC support, independent OS/DDC labels,
refresh changes and failures, and invalidation after an unverified switch.

Run on Windows with .NET 8:

```powershell
dotnet run --project tests/LumaDesk.WorkflowTests
dotnet run --project tests/LumaDesk.UiTests -- --fixtures artifacts/wpf-fixtures
./test.ps1
```

Local checks covered whitespace, localization key/placeholder parity, project XML,
and test registration. C# tests, compilation, WPF rendering, and hardware queries
were not executed: this Linux environment has neither `dotnet` nor `pwsh`.
