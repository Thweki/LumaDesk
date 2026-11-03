// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDesk;

internal sealed record ControlCommand(string Action, string? Target = null, int? Value = null);
internal sealed record ControlReply(bool Success, object? Data = null, string? Error = null);
internal sealed class CommandService : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private static readonly string PipeName = GetPipeName();
    private static string GetPipeName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return "LumaDesk.Control." + identity.User!.Value + "." + process.SessionId;
    }
    internal CommandService(Func<ControlCommand, Task<ControlReply>> execute) => _ = Listen(execute);
    public void Dispose() => stop.Cancel();
    private async Task Listen(Func<ControlCommand, Task<ControlReply>> execute)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var input = await ReadBoundedLine(reader, stop.Token).WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
                ControlReply reply;
                try
                {
                    var command = JsonSerializer.Deserialize<ControlCommand>(input) ?? throw new InvalidDataException("Invalid command.");
                    reply = await execute(command);
                }
                catch (Exception ex) { reply = new(false, Error: ex.Message); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { Storage.Log("Command server: " + ex.Message); await Task.Delay(500); }
        }
    }
    private static async Task<string> ReadBoundedLine(StreamReader reader, CancellationToken cancellation)
    {
        var text = new System.Text.StringBuilder(); var buffer = new char[1];
        while (text.Length < 8192)
        {
            if (await reader.ReadAsync(buffer.AsMemory(), cancellation) == 0) throw new EndOfStreamException();
            if (buffer[0] == '\n') return text.ToString();
            text.Append(buffer[0]);
        }
        throw new InvalidDataException("Command too long.");
    }
    internal static bool IsCommand(string[] args) => args.Length > 0 && args[0] is "--list" or "--brightness" or "--apply-preset";
    internal static (ControlCommand Command, string Output) Parse(string[] args)
    {
        int index = Array.IndexOf(args, "--output");
        if (index < 1 || index != args.Length - 2 || string.IsNullOrWhiteSpace(args[index + 1])) throw new ArgumentException("Use --list | --brightness <display> <0-100> | --apply-preset <name>, followed by --output <result.json>.");
        ControlCommand command = args[0] switch
        {
            "--list" when index == 1 => new("list"),
            "--brightness" when index == 3 && int.TryParse(args[2], out int value) && value >= 0 && value <= 100 => new("brightness", args[1], value),
            "--apply-preset" when index == 2 => new("preset", args[1]),
            _ => throw new ArgumentException("Invalid display command.")
        };
        return (command, Path.GetFullPath(args[index + 1]));
    }
    internal static async Task<int> RunClient(string[] args)
    {
        string? output = null;
        try
        {
            int outputIndex = Array.IndexOf(args, "--output");
            if (outputIndex >= 0 && outputIndex == args.Length - 2) output = Path.GetFullPath(args[outputIndex + 1]);
            var parsed = Parse(args); output = parsed.Output;
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(700); }
            catch (TimeoutException)
            {
                if (parsed.Command.Action == "list")
                {
                    var displays = await new DisplayService().ScanAsync();
                    File.WriteAllText(output, JsonSerializer.Serialize(new ControlReply(true, displays), Storage.Json)); return 0;
                }
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--tray"); using var process = Process.Start(start);
                await pipe.ConnectAsync(20000);
            }
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(parsed.Command));
            // No retry after submission: a timeout does not prove the write failed.
            string response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(2)) ?? throw new IOException("No response.");
            File.WriteAllText(output, response);
            using var json = JsonDocument.Parse(response); return json.RootElement.GetProperty("Success").GetBoolean() ? 0 : 1;
        }
        catch (Exception ex)
        {
            Storage.Log("CLI: " + ex.Message);
            if (output != null)
                try { File.WriteAllText(output, JsonSerializer.Serialize(new ControlReply(false, Error: ex.Message), Storage.Json)); }
                catch (Exception writeError) { Storage.Log("CLI result: " + writeError.Message); }
            return 1;
        }
    }
}
