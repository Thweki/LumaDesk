# Keep displays awake (experimental)

Keep a reference image or dashboard visible without changing Windows power
settings. This feature is **OFF by default** and saves no preferences.

Exit any running LumaDesk instance, then launch from PowerShell:

```powershell
$env:LUMADESK_EXPERIMENTAL_KEEP_AWAKE = '1'
.\LumaDesk.exe
```

Open the tray panel and check **Keep displays awake (experimental)**. Use the
existing **Pin** button if you want the panel to remain open when you use another
app. Pinning alone does not enable keep-awake. The toggle remains accessible when
adjusting several displays together.

The request covers all displays and idle system sleep while the panel is visible.
Uncheck the toggle, hide the panel, or close it (including Escape) to release it.
Opening the panel again starts with the toggle off. A cancelled close retains the
request until the panel actually closes. Exiting the app releases its request.
If Windows rejects the request, the toggle stays off and an error appears below it;
you can retry. To hide the feature, remove the environment variable and restart.

This uses an owned Windows power-request handle, with both DisplayRequired and
SystemRequired requests, rather than changing a power plan or simulating input.
Explicit sleep, closing the lid, and Windows power policies can still take
precedence. See Microsoft's [PowerSetRequest documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-powersetrequest)
and [request-handle lifecycle](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-powercreaterequest).

## Validation

`PanelKeepAwakeTests` exercises flag parsing, default-off behavior, hidden panels,
idempotent acquisition/release, failures, retries, disposal, and the native context
layout. `KeepAwakeTests` in the interactive UI suite exercises the actual toggle,
rerendering, error feedback, hide/show, cancelled close, and reopening. Both use
fake request handles and do not change the machine's power behavior.

On Windows with .NET 8, run `dotnet run --project tests/LumaDesk.Tests` and
`dotnet run --project tests/LumaDesk.UiTests` (the latter needs an interactive
desktop), or run `./test.ps1` for the full suite.

For native verification, enable and pin the panel, check `powercfg /requests` for
the LumaDesk display/system requests, then uncheck or close the panel and verify
both disappear. Also verify idle display sleep resumes after closing the app.

The implementation environment had no `dotnet` or `pwsh`; these C# tests and native
Windows checks were **not executed** there. JSON/localization consistency, project
references, test registration, and whitespace were checked locally.
