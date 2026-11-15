# Workflow tests

Run on Windows with the .NET 8 SDK:

```powershell
dotnet run --project tests/LumaDesk.WorkflowTests -c Release
```

The existing `test.ps1 -Headless` CI step runs this executable, including
`CommandPipeTests`. Use `dotnet run`, not `dotnet test`, for this assertion harness.

The command tests exercise the real listener and CLI over uniquely named local
pipes with a fake executor and temporary result files. Explicit test endpoints
disable the client's display-scan and app-launch fallback. No display writes or
app launches are needed. The stalled-client case exercises the production
five-second request timeout; pipe operations have a fifteen-second test deadline.

Coverage includes successful data replies, failed replies, executor exceptions,
Unicode arguments, one dispatch per command, malformed/null JSON, oversized input,
missing newlines, recovery after rejected clients, and listener shutdown. A fake
server also disconnects after receiving a mutation and checks that the client
reports failure without reconnecting to resubmit it.
