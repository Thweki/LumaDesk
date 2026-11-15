# Reconnect ownership and restoration regressions

Audit recommendation: `reconnect-regressions`. Scope is reconnect brightness/dimming ownership, retries and manual overrides. Recovery lifecycle, ICC scene restoration and CI workflows are unchanged.

`ReconnectCoordinator` owns one restoration from the initial scan through the connection-scene decision. `IReconnectBackend` injects enumeration, brightness writes, dimming and reporting; the retry delay is also injectable. `MainWindow` supplies the production adapter and retains the existing manual brightness/dimming cancellation entry points. The coordinator uses the UI synchronization context, as those entry points do.

The coordinator captures remembered targets before the first asynchronous scan and records manual overrides even before per-display tokens exist. It keeps the same cancellation source across retries, resolves fresh display objects by case-insensitive ID, and checks ownership again after an awaited hardware write. This prevents a late completion from publishing stale brightness or applying saved dimming over a manual replacement. Manual overrides also suppress the pending connection scene. A native write already in progress may still complete; cancellation cannot undo that hardware call.

`tests/LumaDesk.WorkflowTests/ReconnectTests.cs` is invoked by the existing workflow executable. Its fake backend and task gates cover:

- Three bounded attempts, 1s/2s injected backoff, fresh display objects and retained token identity.
- Partial success, exhausted retries, temporarily unsupported brightness and a missing display that returns.
- Manual overrides during the initial scan, retry scan, backoff, an in-flight write and another display's write after the first display has succeeded.
- Replacement of the remembered target during a write even without an explicit cancellation call.
- Overlapping notifications, cancellation, scan exceptions, ownership cleanup and a subsequent wake with fresh tokens.
- Blocking scene/mode activity, unchanged topology, wake restoration, dimming-only targets, disabled brightness restoration and integration-test suppression.

No fake operation sends native writes or waits for real retry timers. Gate completion has a five-second timeout to make stalled continuations fail rather than hang the suite. These checks detect lost ownership through duplicate scans/writes, replaced tokens, premature idle state, stale model updates, incorrect failure reports or an allowed connection scene after manual input.

## Validation in this worktree

On 2026-11-15, `git diff --check` and static source/project wiring checks passed. `dotnet --info` failed because `dotnet` is not installed; `csc`, `mcs` and PowerShell (`pwsh`) are also unavailable. The new C# code has **not been compiled or executed** here, and no hardware tests were run.

Windows CI must run the new tests before merge:

```powershell
dotnet run --project tests/LumaDesk.WorkflowTests -c Release
./test.ps1 -Headless
```

The workflow project references the production project and automatically includes the new test source. The existing `test.ps1` already invokes it; no CI or test-runner changes are needed. Real hotplug, sleep/wake and DDC/WMI timing still need hardware validation.
