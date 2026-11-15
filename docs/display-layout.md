# 屏幕排列

支持拖动预览、边缘吸附、输出像素坐标、主屏选择、横向/纵向排列，以及扩展/复制拓扑。预览不写系统；点击应用后才提交。

坐标取自 EnumDisplaySettings 的原生模式，与分辨率使用相同物理像素单位。主屏位置归零，其余屏幕整体平移，保留相对关系。纯规则拒绝重叠、断开、重复设备及多个主屏。

读取完整 CCD 路径和 64 字节模式联合体，仅修改源位置，先使用 SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG 验证整套排列，再整体应用保存。不能逐屏单独验证主屏迁移：其他屏幕未移动时会误判完整布局。

应用后回读位置、尺寸和主屏标识；失败恢复原模式与排列，另有超时恢复进程。设备身份变化时拒绝写入替代设备。共享源需先切换扩展才能独立排列。

## 实验性：交换两个屏幕的位置 / Experimental: swap display positions

默认关闭。退出正在运行的 LumaDesk，然后从 PowerShell 启用当前进程的实验标志并启动：

```powershell
$env:LUMADESK_EXPERIMENTAL_SWAP = '1'
.\LumaDesk.exe
```

打开屏幕排列，选择一个屏幕，在“交换对象”中选择另一个屏幕，点击“交换位置（实验性）”。检查预览后点击应用。未点击应用前不会更改系统排列；关闭窗口可放弃预览更改。删除环境变量并重启即可隐藏此功能，不写入用户设置。

Disabled by default. Exit the running app, then launch it with `LUMADESK_EXPERIMENTAL_SWAP=1` as above. In **Display layout**, select a screen, choose another under **Swap with**, and click **Swap positions (experimental)**. Review the preview, then click **Apply**. Swapping only edits the preview; close the window before applying to discard preview changes. Unset the variable and restart to hide the feature. No preference file changes are needed.

Each screen keeps its resolution, orientation, scaling, brightness and primary status. Adjacent screens with different dimensions exchange places while keeping the pair's outer edges along their shared axis. Other pairs exchange their top-left positions. All coordinates are translated together to keep the primary at `(0, 0)`; other screens retain their relative positions. A swap that would overlap or disconnect any screen is rejected without changing the preview. Mirrored screens must be extended first. Apply uses the existing whole-layout validation, identity checks, readback and rollback path.

Regression coverage: `LayoutSwapTests` in the logic and UI projects covers unequal horizontal/vertical pairs, primary translation, third-screen preservation, invalid arrangements, default-off behavior and preview/apply separation. UI fixtures inject the apply callback and perform no hardware writes. Run `./test.ps1` and `dotnet run --project tests/LumaDesk.UiTests -- --fixtures` on Windows. These tests were **not executed in the implementation environment** because `dotnet` and `pwsh` are unavailable; hardware validation remains required.

本次执行了三屏替换主屏后的完整 SDC_VALIDATE，未真正移动桌面。实际应用、失败恢复、热插拔和重启持久化仍需实机覆盖。

来源：[SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)、[源模式与主屏定义](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_source_mode)、[多屏批量恢复](https://devblogs.microsoft.com/oldnewthing/20211222-00/?p=106048)。
