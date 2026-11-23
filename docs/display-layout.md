# 屏幕排列

支持拖动预览、边缘吸附、输出像素坐标、主屏选择、横向/纵向排列，以及扩展/复制拓扑。预览不写系统；点击应用后才提交。

排列窗口支持“撤销预览 / 重做预览”按钮及 `Ctrl+Z` / `Ctrl+Y`：可撤销或重做拖动、更新坐标、横向/纵向排列和主屏选择。坐标文本框中的快捷键仍用于编辑文字；点击按钮可撤销整个预览步骤。成功应用、重新读取屏幕或切换拓扑后清空预览历史，应用失败则保留历史以便修改。此功能不撤销已应用的系统排列。

The layout editor offers **Undo preview / Redo preview** (`Ctrl+Z` / `Ctrl+Y`) for drags, coordinate updates, arrangements and primary-screen choices. In coordinate text fields, these shortcuts retain normal text editing behavior; use the buttons to undo a whole preview step. Successful apply, reload or topology changes clear preview history; failed apply preserves it for correction. Undo/redo only changes the preview, never an already applied desktop layout.

精确相邻放置：先选择要移动的屏幕，再选择参考屏幕、左侧/右侧/上方/下方以及边缘或居中对齐，点击「在预览中放置」。左右放置支持顶部/居中/底部对齐，上下放置支持左侧/居中/右侧对齐。仅移动所选屏幕；其他屏幕与主屏标识保持不变。最后点击应用，验证完整排列后再提交。与第三块屏幕重叠或断开的预览仍会被拒绝。

### Precise relative placement

In the display layout editor, select the screen to move, then choose a neighbor under **Position selected display relative to**. Choose **Left of**, **Right of**, **Above**, or **Below**, and an edge or center alignment. Click **Place in preview**, then **Apply** when the full layout is ready.

This closes the gap to that neighbor using output pixels, including for mixed portrait/landscape screens. Left/right placement aligns top edges, centers, or bottom edges; above/below placement aligns left edges, centers, or right edges. Half-pixel center offsets round toward the top/left. Other screens and the primary designation stay unchanged. Placement only edits the preview; Apply still rejects overlaps or disconnected screens and uses the existing rollback-protected layout transaction. At least two displays are required.

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
