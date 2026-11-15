# 屏幕排列

支持拖动预览、边缘吸附、输出像素坐标、主屏选择、横向/纵向排列，以及扩展/复制拓扑。预览不写系统；点击应用后才提交。

坐标取自 EnumDisplaySettings 的原生模式，与分辨率使用相同物理像素单位。主屏位置归零，其余屏幕整体平移，保留相对关系。纯规则拒绝重叠、断开、重复设备及多个主屏。

读取完整 CCD 路径和 64 字节模式联合体，仅修改源位置，先使用 SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG 验证整套排列，再整体应用保存。不能逐屏单独验证主屏迁移：其他屏幕未移动时会误判完整布局。

应用后回读位置、尺寸和主屏标识；失败恢复原模式与排列，另有超时恢复进程。设备身份变化时拒绝写入替代设备。共享源需先切换扩展才能独立排列。

本次执行了三屏替换主屏后的完整 SDC_VALIDATE，未真正移动桌面。实际应用、失败恢复、热插拔和重启持久化仍需实机覆盖。

来源：[SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)、[源模式与主屏定义](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_source_mode)、[多屏批量恢复](https://devblogs.microsoft.com/oldnewthing/20211222-00/?p=106048)。

## Experimental tidy preview / 实验性自动整理

Disabled by default. To try it, fully exit LumaDesk (including the tray instance),
then launch it from a PowerShell session with the process flag enabled:

```powershell
$env:LUMADESK_EXPERIMENTAL_AUTOLAYOUT = '1'
.\LumaDesk.exe
```

Open the display layout editor and click **Tidy preview (experimental)**. Review
the preview, then click **Apply** to submit it through the usual layout validation
and recovery path. Closing the editor discards the preview; **Refresh** reloads
the current display positions. No preference file is changed to enable this feature.
Only the exact flag value `1` enables the button. Exit and relaunch without that
environment variable to disable it.

Tidy keeps the selected primary at `(0, 0)` and preserves display identity,
dimensions, and primary selection. Already valid connected layouts stay unchanged.
For overlapping or disconnected previews, it places displays nearest the primary
first, choosing nearby free edges to retain the general spatial arrangement
(including screens above or to the left), rather than forcing a single row or column.
Every returned preview is connected and has no overlapping interiors. This is a
deterministic greedy planner, not a globally optimal packing algorithm; if its
candidates exceed the supported coordinate range, it reports an error and leaves
the preview untouched. It does not change resolution, clone/extend topology, or
run automatically at startup or on hotplug. Mirrored outputs must first be extended
to apply independent positions.

默认关闭。设置上述环境变量并重新启动后，可在屏幕排列窗口使用“自动整理预览（实验性）”。
整理仅修改预览；检查后点击应用才会移动屏幕。保留主屏和屏幕尺寸，消除重叠和间隙，
并尽量保留上下左右关系。找不到可用排列时保留原预览；不会在启动或热插拔时自动应用。

Coverage: `AutoLayoutTests` in the logic suite and UI fixtures exercise opt-in,
geometry, extreme coordinates, deterministic placement, preview isolation, Apply,
and failure preservation. Run on Windows with:

```powershell
dotnet run --project tests/LumaDesk.Tests -c Release
dotnet run --project tests/LumaDesk.UiTests -c Release -- --fixtures
```

These C# tests were added but not executed in the implementation environment:
`dotnet` and `pwsh` were unavailable. Hardware apply still needs Windows validation.
