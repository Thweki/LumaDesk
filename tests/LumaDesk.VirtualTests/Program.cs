// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 LumaDesk contributors

using LumaDesk;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

int count = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); count++; }
Check(VirtualDisplayService.CountCommand(1) == "SETDISPLAYCOUNT 1" && VirtualDisplayService.CountCommand(8) == "SETDISPLAYCOUNT 8", "Bounded exact pipe commands");
foreach (int invalid in new[] { -1, 0, 9 }) { bool rejected = false; try { VirtualDisplayService.CountCommand(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; } Check(rejected, "Invalid live count rejected (zero requires device disable)"); }
Check(VirtualDisplayService.ParseConfiguredCount("<vdd_settings><monitors><count>3</count></monitors></vdd_settings>") == 3, "Count from exact XML location");
Check(VirtualDisplayService.ParseConfiguredCount("<vdd_settings><monitors><count>-1</count></monitors></vdd_settings>") == null, "Negative persisted count rejected");
bool dtd = false; try { VirtualDisplayService.ParseConfiguredCount("<!DOCTYPE vdd_settings [<!ENTITY a '2'>]><vdd_settings><monitors><count>&a;</count></monitors></vdd_settings>"); } catch (XmlException) { dtd = true; } Check(dtd, "DTD prohibited");
Check(!DriverInstaller.MatchesHash(Encoding.UTF8.GetBytes("tampered"), DriverInstaller.ArchiveHash), "Changed package bytes rejected");
foreach (var shape in new[] { ("DeviceInfo", 32), ("PropertyChange", 20), ("TrustFile", 32), ("TrustData", 88) })
    Check(Marshal.SizeOf(typeof(DriverInstaller).GetNestedType(shape.Item1, BindingFlags.NonPublic)!) == shape.Item2, "x64 native layout " + shape.Item1);
string pipeName = "LumaDeskTest-" + Guid.NewGuid().ToString("N");
using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
{
    var serverTask = Task.Run(async () => { await server.WaitForConnectionAsync(); byte[] buffer = new byte[64]; int n = await server.ReadAsync(buffer); Check(Encoding.Unicode.GetString(buffer, 0, n) == "PING\0", "Wire command is UTF16LE null-terminated"); await server.WriteAsync(Encoding.UTF8.GetBytes("PONG")); server.Disconnect(); });
    using var timeout = new CancellationTokenSource(3000);
    Check(await VirtualDisplayService.SendPipeCoreAsync("PING", timeout.Token, pipeName) == "PONG", "One-shot upstream response read through disconnect"); await serverTask;
}
bool canceled = false; using (var timeout = new CancellationTokenSource(50)) { try { await VirtualDisplayService.SendPipeCoreAsync("PING", timeout.Token, "LumaDeskAbsent-" + Guid.NewGuid().ToString("N")); } catch (OperationCanceledException) { canceled = true; } } Check(canceled, "Absent pipe cancellation works");
if (args.Contains("--verify-download"))
{
    string package = await DriverInstaller.PreparePackageAsync();
    Check(File.Exists(Path.Combine(package, "MttVDD.inf")), "Pinned official download extracted with hashes and Windows trust checks");
    Console.WriteLine("PACKAGE " + package);
}
var state = await new VirtualDisplayService().ReadAsync();
Console.WriteLine("READONLY " + System.Text.Json.JsonSerializer.Serialize(state));
Console.WriteLine(count + " virtual-driver checks passed; no install or monitor writes.");

namespace LumaDesk { internal static class Storage { public static string Folder => Path.GetFullPath("artifacts/virtual-driver-tests"); public static void Log(string message) => Console.WriteLine(message); } }
