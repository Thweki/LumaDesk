using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LumaDesk;

public partial class App : System.Windows.Application
{
    internal bool IntegrationTest { get; set; }
    private Mutex? mutex;
    private EventWaitHandle? activateEvent;
    private RegisteredWaitHandle? activationRegistration;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (IntegrationTest) return;
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        if (e.Args.Length == 2 && e.Args[0] == "--install-color-profile")
        {
            try { AdvancedColorService.InstallProfileOnly(e.Args[1]); Shutdown(0); }
            catch (Win32Exception ex) { Shutdown(ex.NativeErrorCode == 0 ? 31 : ex.NativeErrorCode); }
            catch { Shutdown(87); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--install-virtual-driver")
        {
            try { DriverInstaller.InstallVerifiedPackage(e.Args[1]); Shutdown(0); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "虚拟屏幕驱动安装未完成"); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--set-virtual-driver-enabled" && (e.Args[1] == "true" || e.Args[1] == "false"))
        {
            try { DriverInstaller.SetDriverEnabled(e.Args[1] == "true"); Shutdown(0); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "虚拟屏幕状态未能更新"); Shutdown(1); }
            return;
        }
        if (e.Args.Length >= 2 && (e.Args[0] == "--watch-mode" || e.Args[0] == "--watch-dpi" || e.Args[0] == "--watch-layout"))
        {
            for (int i = 0; i < 120 && File.Exists(e.Args[1]); i++) await Task.Delay(250);
            if (e.Args[0] == "--watch-dpi") DpiRecovery.RestoreFile(e.Args[1]); else if (e.Args[0] == "--watch-layout") LayoutService.RestoreFile(e.Args[1]); else ModeRecovery.RestoreFile(e.Args[1]);
            Shutdown(); return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--diagnostics")
        {
            try { var displays = await new DisplayService().ScanAsync(); File.WriteAllText(e.Args[1], JsonSerializer.Serialize(displays, Storage.Json)); Shutdown(0); }
            catch (Exception ex) { Storage.Log(ex.ToString()); Shutdown(1); }
            return;
        }
        mutex = new Mutex(true, "Local\\LumaDesk.Desktop", out var fresh);
        activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LumaDesk.Activate");
        if (!fresh) { activateEvent.Set(); Shutdown(); return; }
        activationRegistration = ThreadPool.RegisterWaitForSingleObject(activateEvent, (_, _) => Dispatcher.BeginInvoke(() => { if (MainWindow is MainWindow main) main.ShowQuick(); }), null, Timeout.Infinite, false);
        DispatcherUnhandledException += (_, args) => { Storage.Log(args.Exception.ToString()); MessageBox.Show(args.Exception.Message, "操作未完成"); args.Handled = true; };
        var window = new MainWindow(); MainWindow = window;
        window.Show(); window.Hide();
        if (!e.Args.Contains("--tray")) window.ShowQuick();
    }
    protected override void OnExit(ExitEventArgs e) { activationRegistration?.Unregister(null); activateEvent?.Dispose(); mutex?.Dispose(); base.OnExit(e); }
}
internal sealed class DpiRecovery
{
    public string Device { get; set; } = "";
    public string Id { get; set; } = "";
    public int OriginalPercent { get; set; }
    public static string Prepare(DisplayInfo display, int originalPercent)
    {
        Directory.CreateDirectory(Storage.Folder);
        string path = Path.Combine(Storage.Folder, "dpi-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new DpiRecovery { Device = display.Device, Id = display.Id, OriginalPercent = originalPercent }, Storage.Json));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--watch-dpi"); start.ArgumentList.Add(path);
        try { using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动缩放恢复进程。"); }
        catch { File.Delete(path); throw; }
        return path;
    }
    public static void RestoreFile(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var recovery = JsonSerializer.Deserialize<DpiRecovery>(File.ReadAllText(path), Storage.Json)!;
            DpiService.Set(new DisplayInfo { Device = recovery.Device, Id = recovery.Id }, recovery.OriginalPercent);
            File.Delete(path);
        }
        catch (Exception ex) { Storage.Log("DPI recovery failed: " + ex); }
    }
}
internal sealed class ModeRecovery
{
    public string Device { get; set; } = "";
    public string Id { get; set; } = "";
    public Native.DevMode Original { get; set; }
    public static string Prepare(string device, string id, Native.DevMode original)
    {
        Directory.CreateDirectory(Storage.Folder);
        var path = Path.Combine(Storage.Folder, "mode-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new ModeRecovery { Device = device, Id = id, Original = original }, Storage.Json));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--watch-mode"); start.ArgumentList.Add(path);
        try { using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动显示模式恢复进程。"); }
        catch { File.Delete(path); throw; }
        return path;
    }
    public static void RestoreFile(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var recovery = JsonSerializer.Deserialize<ModeRecovery>(File.ReadAllText(path), Storage.Json)!;
            DisplayService.ApplyMode(recovery.Device, recovery.Original, expectedId: recovery.Id);
            File.Delete(path);
        }
        catch (Exception ex) { Storage.Log("Mode recovery failed: " + ex); }
    }
}
