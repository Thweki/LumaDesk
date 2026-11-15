// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using LumaDesk;

internal static class CommandPipeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    internal static async Task Run(Action<bool, string> check)
    {
        string pipeName = "LumaDesk.Tests.Control." + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Path.GetTempPath(), pipeName);
        Directory.CreateDirectory(folder);
        var commands = new ConcurrentQueue<ControlCommand>();
        using var service = new CommandService(command =>
        {
            commands.Enqueue(command);
            return command.Action switch
            {
                "list" => Task.FromResult(new ControlReply(true, new[] { "fake-display" })),
                "preset" => Task.FromResult(new ControlReply(false, Error: "Unknown preset")),
                _ => throw new InvalidOperationException("Write failed")
            };
        }, pipeName);
        try
        {
            async Task<JsonDocument> RunClient(int expectedExit, params string[] args)
            {
                string output = Path.Combine(folder, "result.json");
                File.Delete(output);
                int exit = await CommandService.RunClient(args.Concat(new[] { "--output", output }).ToArray(), pipeName).WaitAsync(Deadline);
                check(exit == expectedExit && File.Exists(output), "Pipe client writes its result and returns the expected exit code");
                return JsonDocument.Parse(File.ReadAllText(output));
            }

            using (var reply = await RunClient(0, "--list"))
                check(reply.RootElement.GetProperty("Success").GetBoolean() && reply.RootElement.GetProperty("Data")[0].GetString() == "fake-display",
                    "Listener and CLI roundtrip a successful data reply");
            using (var reply = await RunClient(1, "--apply-preset", "夜间"))
                check(!reply.RootElement.GetProperty("Success").GetBoolean() && reply.RootElement.GetProperty("Error").GetString() == "Unknown preset",
                    "Listener and CLI preserve failed replies");
            using (var reply = await RunClient(1, "--brightness", "A", "42"))
                check(!reply.RootElement.GetProperty("Success").GetBoolean() && reply.RootElement.GetProperty("Error").GetString() == "Write failed",
                    "Executor exceptions become failed replies");
            check(commands.ToArray().SequenceEqual(new[] { new ControlCommand("list"), new ControlCommand("preset", "夜间"), new ControlCommand("brightness", "A", 42) }),
                "Each submitted command executes once with its original arguments");

            foreach (string input in new[] { "{", "null" })
            {
                string? response = await Exchange(pipeName, input + "\n");
                using var reply = JsonDocument.Parse(response ?? throw new Exception("Missing invalid-command reply"));
                check(!reply.RootElement.GetProperty("Success").GetBoolean() && commands.Count == 3,
                    "Malformed or null JSON is rejected without dispatch");
            }
            check(await Exchange(pipeName, new string('x', 8192) + "\n") == null && commands.Count == 3,
                "Oversized requests are disconnected without dispatch");
            using (var reply = JsonDocument.Parse(await Exchange(pipeName, "{\"Action\":\"list\"}\n") ?? throw new Exception("Missing recovery reply")))
                check(reply.RootElement.GetProperty("Success").GetBoolean() && commands.Count == 4,
                    "Listener accepts another command after rejecting oversized input");

            check(await Exchange(pipeName, "{\"Action\":\"list\"}") == null && commands.Count == 4,
                "A connected client without a newline times out without dispatch");
            using (var reply = JsonDocument.Parse(await Exchange(pipeName, "{\"Action\":\"list\"}\n") ?? throw new Exception("Missing recovery reply")))
                check(reply.RootElement.GetProperty("Success").GetBoolean() && commands.Count == 5,
                    "Listener accepts another command after a stalled client");

            service.Dispose();
            await service.Completion.WaitAsync(Deadline);
            check(service.Completion.IsCompletedSuccessfully, "Disposal stops an idle listener");
            await LostReply(pipeName + ".lost", folder, check);
        }
        finally
        {
            service.Dispose();
            await service.Completion.WaitAsync(Deadline);
            Directory.Delete(folder, true);
        }
    }

    private static async Task<string?> Exchange(string pipeName, string input)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            // Leave the connection open while reading, including the no-newline case.
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);
            await pipe.WriteAsync(bytes, timeout.Token);
            return await reader.ReadLineAsync(timeout.Token);
        }
        catch (IOException) { return null; } // Rejection can close or reset the pipe during write or read.
    }

    private static async Task LostReply(string pipeName, string folder, Action<bool, string> check)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        string output = Path.Combine(folder, "lost.json");
        var client = CommandService.RunClient(new[] { "--brightness", "A", "42", "--output", output }, pipeName);
        await server.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(server, leaveOpen: true);
        string? request = await reader.ReadLineAsync(timeout.Token);
        check(JsonSerializer.Deserialize<ControlCommand>(request ?? "null") == new ControlCommand("brightness", "A", 42),
            "Lost-reply fixture receives the mutation before disconnecting");
        server.Disconnect();
        // Keep a server available so an unsafe retry would connect and be observed.
        var retry = server.WaitForConnectionAsync(timeout.Token);
        int exit = await client.WaitAsync(Deadline);
        check(exit == 1 && !retry.IsCompleted, "Client does not resubmit a mutation when its reply is lost");
        using var reply = JsonDocument.Parse(File.ReadAllText(output));
        check(!reply.RootElement.GetProperty("Success").GetBoolean(), "Lost reply produces a failed result file");
        timeout.Cancel();
        try { await retry; } catch (OperationCanceledException) { }
    }
}
