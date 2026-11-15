# 预设与自动化 / Presets and automation

日常使用只需要调整屏幕，然后点“保存当前设置”。从托盘面板的“预设”选择名称即可切换。更多选项默认收起，自动触发默认关闭，不需要先配置规则。

## 场景与恢复

- 新预设记录设备 ID、亮度/调暗、输出模式、旋转、DPI、可读取的 HDR/ICC 默认配置、主屏和坐标。旧预设只恢复原有亮度数据，不补写其他设置。
- 缺席屏幕跳过，其他屏幕继续执行；明细保留在预设页。只有屏幕集合、模式及旋转匹配且排列通过验证时才恢复排列。不会创建缺席屏幕、启停驱动或安装 ICC 文件。
- 模式、DPI、HDR 和排列沿用各自的验证/失败恢复路径。场景是有序的多项操作，不是系统级原子事务；部分成功不会伪装成全部完成。
- “撤销上次切换”保存本次会话切换前的快照。撤销部分失败时保留快照以便重试；退出后不保留撤销历史。
- 重连/唤醒恢复亮度默认开启，只恢复在本应用调整过的设备。最多尝试三次；手动调整会取代旧恢复目标。更换端口造成设备 ID 变化时不会猜测映射。
- 自动恢复亮度不等于恢复完整场景。后者需在预设的更多选项中明确开启“连接这些屏幕时使用”，且屏幕集合必须完全匹配。

## 捕获为草稿 / Capture a live setup as a draft

在“预设”页点击“捕获当前配置”，即可读取当前连接屏幕的设置，生成尚未保存的预设草稿。检查屏幕摘要、输入名称，然后点击“保存草稿”或“丢弃草稿”。保存使用捕获时的设置，不会再次读取或应用屏幕设置；之后调整屏幕或断开连接不会改变草稿。草稿仅在本次运行期间保留，切换页面不会丢失，退出应用后丢弃。一次只保留一份草稿；保存失败可重试，自动触发和快捷键默认关闭。

On **Presets**, click **Capture current setup** to read the connected displays into an **Unsaved preset**. Review the screen summary, edit the name, then **Save draft** or **Discard draft**. Capture reads the same settings supported by presets: brightness/dimming, output mode, rotation, scaling, readable HDR/ICC state, primary display and coordinates. Unavailable values remain unspecified. Capture, save and discard do not apply display settings. Saving uses the captured values without another scan, even after screens change or disconnect. Names must contain 1–40 characters and be unique, ignoring case and surrounding whitespace. One draft is kept in memory across page navigation and discarded on exit. Failed saves retain it for retry. Saved drafts start with automatic triggers and shortcuts off. No experimental flag is required.

## 复制预设 / Duplicate a preset

在“预设”页展开已有预设的“更多选项”，输入“新预设名称”，点击“复制预设”。名称为 1–40 个字符，不能与已有名称重复（不区分大小写）。副本拥有独立 ID，复制已保存的显示设置，无需连接原来的屏幕，也不会读取或应用当前设置。自动触发和快捷键默认关闭，可在副本的更多选项中单独配置；原预设及其自动化保持不变。

On the **Presets** page, expand **More options** for an existing preset, enter a **New preset name**, and click **Duplicate preset**. Names must contain 1–40 characters and be unique, ignoring case and surrounding whitespace. The copy gets its own ID and saved display settings, even if those screens are disconnected. Duplication does not capture or apply the current setup. Automatic triggers and the shortcut start off; configure them separately on the copy if needed. The original preset and its automation stay unchanged. No experimental flag is required.

## 排序预设 / Reorder presets

在“预设”页点击预设卡片上的“上移”或“下移”，即可将常用预设放在前面。顺序会自动保存，重启后保留，托盘预设菜单也使用同一顺序。排序不读取或应用屏幕设置，也不改变预设 ID、显示设置、快捷键或自动触发配置。多个自动化规则同时匹配时，仍按列表顺序选择第一个匹配项。

On the **Presets** page, use **Move up** or **Move down** on a preset card to put frequently used presets first. The order is saved immediately, survives restarts, and also appears in the tray preset menu. Reordering does not capture or apply display settings or change preset IDs, saved values, shortcuts, or trigger settings. Existing automation still selects the first matching preset in list order when multiple rules match. No experimental flag is required.

## 可选自动化

预设可设置每天的时间（本机 24 小时制）或接通电源/使用电池触发。应用运行期间约每 20 秒检查一次；休眠错过的最近一次时间可在恢复后触发，不补跑历史任务。同一轮多个预设命中时只应用列表中第一个匹配屏幕集合的预设。

设置、托盘、快捷键和命令行共用 0–100% 的亮度刻度。支持背光的屏幕在 20% 达到最低背光，继续向左拖动会用软件调暗。0% 仍能看到画面。没有背光接口的屏幕全程用软件调节。旧预设同时设置了背光和调暗时，调亮会先减弱调暗，调暗会先降低背光。

分组同步使用同一刻度，正偏移表示相对其他屏幕更亮，上下限限定各屏的目标百分比。亮度快捷键默认作用于鼠标所在屏幕，录入快捷键时临时停止全局按键处理。

托盘面板中的“联调亮度”用于临时多选：勾选的屏幕只接受亮度调整，不沿用组成员或相对偏移，仍遵守各屏亮度上下限。未选择屏幕时滑块不可用；退出联调恢复此前的单屏控制，不创建或修改显示器组。关闭面板后，下次默认进入单屏模式。

## 命令行

这是脚本接口；普通用户无需使用。所有命令要求结果文件，返回 JSON，成功退出码为 0，失败/部分完成为 1。

```powershell
$app = '.\LumaDesk.exe'
& $app --list --output .\screens.json
& $app --list-presets --output .\presets.json
& $app --brightness '1' 60 --output .\brightness-result.json
& $app --apply-preset '办公' --output .\preset-result.json
```

显示器参数可以是列表中的设备 ID、唯一名称或编号。长期脚本优先使用设备 ID；编号可能随连接变化。亮度命令遵循应用内分组和同步设置。预设可使用名称或 ID。

`--list-presets` 只读列出已保存的预设，按应用内顺序返回 `Data` 数组；每项含 `Id`、`Name` 和 `Displays`（已保存屏幕的 `Id`、`Name`）。未连接屏幕也会列出；没有预设时成功返回空数组。应用运行时读取内存中的预设，否则读取本地已保存的预设，不启动托盘应用或访问显示器。

For scripts, `--list-presets` discovers saved preset IDs without changing any display settings. The JSON reply has `Success`, `Data`, and `Error`; `Data` contains `{ "Id", "Name", "Displays": [{ "Id", "Name" }] }` entries in library order, including disconnected displays. An empty library returns success with `Data: []`. It uses the running app's library when available, otherwise reads saved preferences without starting the app or probing monitors. Use an entry's stable `Id` with `--apply-preset` so renaming the preset does not break the script:

```powershell
$presets = Get-Content .\presets.json -Raw | ConvertFrom-Json
if ($presets.Success -and $presets.Data.Count -gt 0) {
    & $app --apply-preset $presets.Data[0].Id --output .\preset-result.json
}
```

已有应用时，命令通过限定当前 Windows 用户及桌面会话的本地命名管道交给应用执行；不会另建调暗覆盖层。未运行时，查询直接读取，修改命令启动托盘应用后再执行。发送命令后不自动重试，避免超时导致重复写入。管道不对网络开放。

## 硬件与性能边界

DDC 调用按设备串行，不同设备的探测及组合亮度同步可并行；实际延迟仍受显卡驱动和显示器限制。慢调用记录耗时。输入源能力仅在展开控件时读取，并缓存五分钟；列表来自能力串，不猜测不存在的 HDMI/DP 端口。参照 [Microsoft 低层显示器配置 API](https://learn.microsoft.com/en-us/windows/win32/monitor/using-the-low-level-monitor-configuration-functions)。输入切换成功返回后可能立即断开当前电脑，故不继续回读或自动重试。

画中画沿用 GDI SDR 捕获，在后台执行并复用图像缓冲；窗口最小化或暂停时停止捕获，不积压帧。可以拖选预览区域或返回完整画面。帧率选项是目标上限，取决于桌面大小和系统负载，不代表 HDR 或视频级低延迟捕获。

自动化测试使用模拟写入或真实只读枚举。DDC、HDR、模式切换、ICC、恢复与输入源的实际效果仍需硬件验证，见 [验证记录](validation.md)。
