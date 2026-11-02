using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LumaDesk;

internal sealed class VirtualDisplayWindow : Window
{
    private readonly MainWindow owner;
    private readonly VirtualDisplayService service;
    private readonly StackPanel body = new() { Margin = new Thickness(20) };
    private readonly TextBlock feedback = Ui.Text("正在检测虚拟显示驱动…", 12, "#68768A");
    private bool busy;
    internal VirtualDisplayWindow(MainWindow owner, VirtualDisplayService? service = null)
    {
        this.owner = owner; this.service = service ?? new VirtualDisplayService();
        Title = "虚拟屏幕 · LumaDesk"; Width = 540; Height = 530; MinWidth = 440; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new ScrollViewer { Content = body };
        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private async Task RefreshAsync(string? message = null)
    {
        body.Children.Clear(); body.Children.Add(Ui.Text("虚拟屏幕", 21, "#28364A", true));
        var description = Ui.Text("在此创建或移除真正的 Windows 扩展屏幕，再在本应用中调整位置、分辨率、缩放与 HDR。", 12, "#7A8595"); description.Margin = new Thickness(0, 9, 0, 16); body.Children.Add(description);
        try
        {
            VirtualDisplayState state = await service.ReadAsync();
            var panel = new StackPanel();
            panel.Children.Add(Ui.Row(Ui.Labels("Virtual Display Driver", state.Message), Ui.Badge(state.Connected ? "已连接" : state.Installed ? "待就绪" : "未安装")));
            panel.Children.Add(Ui.Line());
            panel.Children.Add(Ui.Labels("活动虚拟屏幕", state.ActiveCount + " 台" + (state.ConfiguredCount.HasValue ? $" · 驱动配置 {state.ConfiguredCount} 台" : "")));
            if (!state.Installed)
            {
                var install = Ui.Button("下载并安装虚拟显示驱动", () => _ = InstallAsync(), true); install.Margin = new Thickness(0, 15, 0, 0);
                System.Windows.Automation.AutomationProperties.SetAutomationId(install, "virtual-driver-install");
                panel.Children.Add(install);
                var note = Ui.Text($"使用官方签名的 VirtualDrivers {DriverInstaller.Version} x64 驱动。下载约 129 KB；安装时 Windows 会请求管理员授权。安装完成后暂停驱动，待你创建屏幕。", 11, "#7A8595"); note.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(note);
            }
            else
            {
                var count = new ComboBox { ItemsSource = Enumerable.Range(0, VirtualDisplayService.MaximumDisplays + 1), SelectedItem = state.Disabled ? 0 : Math.Clamp(state.ConfiguredCount ?? state.ActiveCount, 0, VirtualDisplayService.MaximumDisplays), MinWidth = 70, Width = 80 };
                System.Windows.Automation.AutomationProperties.SetAutomationId(count, "virtual-display-count");
                var set = Ui.Button("应用数量", () => _ = SetCountAsync((int)count.SelectedItem), true);
                System.Windows.Automation.AutomationProperties.SetAutomationId(set, "virtual-display-apply-count");
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), IsEnabled = state.Connected || state.Disabled };
                row.Children.Add(count); set.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(set); panel.Children.Add(row);
                var note = Ui.Text("设为 0 会停用驱动、移除其虚拟屏幕；从 0 恢复及归零时需要管理员授权。数量变化会短暂重新连接虚拟屏幕，窗口可能移动。", 11, "#7A8595"); note.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(note);
                panel.Children.Add(Ui.Line());
                var actions = Ui.Row(Ui.Button("在应用中排列屏幕", owner.ShowLayout), Ui.Button("重新检测", () => _ = RefreshAsync())); panel.Children.Add(actions);
            }
            body.Children.Add(Ui.Card(panel));
            var limit = Ui.Text("虚拟屏幕由独立的签名驱动提供。创建虚拟屏幕不会自动把实体屏幕转换成 macOS 的 2× HiDPI 渲染；如需观看虚拟桌面，可在其设备控制中打开画中画。", 11, "#7A8595"); body.Children.Add(limit);
            feedback.Text = message ?? (state.Installed ? "" : "安装仅在点击上面的按钮后开始。"); feedback.Margin = new Thickness(0, 15, 0, 0); body.Children.Add(feedback);
        }
        catch (Exception ex) { feedback.Text = ex.Message; if (!body.Children.Contains(feedback)) body.Children.Add(feedback); }
    }
    private async Task InstallAsync()
    {
        if (busy) return; busy = true; body.IsEnabled = false; string message;
        try
        {
            feedback.Text = "正在下载官方驱动并检查 SHA-256 和 Windows 签名…";
            string package = await DriverInstaller.PreparePackageAsync();
            feedback.Text = "校验通过，正在请求管理员安装…";
            await DriverInstaller.InstallAsync(package);
            await Task.Delay(1500); await owner.RefreshAsync();
            message = "安装操作已完成。驱动就绪后选择需要的虚拟屏幕数量。";
        }
        catch (Exception ex) { message = ex.Message; Storage.Log("Virtual driver install: " + ex); }
        finally { busy = false; body.IsEnabled = true; }
        await RefreshAsync(message);
    }
    private async Task SetCountAsync(int count)
    {
        if (busy) return; busy = true; body.IsEnabled = false; string message;
        try { feedback.Text = "正在调整虚拟屏幕数量并等待系统确认…"; await service.SetCountAsync(count); message = "系统已确认 " + count + " 台活动虚拟屏幕。"; }
        catch (Exception ex) { message = ex.Message; Storage.Log("Virtual monitor count: " + ex); }
        finally { busy = false; body.IsEnabled = true; await owner.RefreshAsync(); }
        await RefreshAsync(message);
    }
}
