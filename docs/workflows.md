# 预设与自动化 / Presets and automation

日常使用只需要调整屏幕，然后点“保存当前设置”。从托盘面板的“预设”选择名称即可切换。更多选项默认收起，自动触发默认关闭，不需要先配置规则。

在“设置 → 预设 → 更多选项”中编辑名称，然后点击“重命名”，即可只修改名称；不会读取或覆盖已保存的屏幕设置，也不要求连接原来的屏幕。预设 ID、快捷键、自动触发规则和顺序保持不变。名称会去除首尾空白，不能为空，最多 40 个字符，且不能与其他预设重名（不区分大小写）。“用当前设置更新”仍用于重新记录屏幕设置。按名称调用的脚本需使用新名称；按 ID 调用的脚本无需修改。

To rename a preset, open **Settings → Presets → More options**, edit its name, and click **Rename**. No enablement is required. This changes only the name, even with the saved displays disconnected; the ID, shortcut, automation and saved display settings stay intact. Names are trimmed, limited to 40 characters and must be nonempty and unique (case-insensitive). **Update from current setup** still captures display settings. Scripts using a name must use the new name; scripts using the preset ID continue to work.

## 场景与恢复

- 新预设记录设备 ID、亮度/调暗、可读取的对比度、输出模式、旋转、DPI、可读取的 HDR/ICC 默认配置、主屏和坐标。旧预设只恢复原有亮度数据，不补写其他设置。
- 缺席屏幕跳过，其他屏幕继续执行；明细保留在预设页。只有屏幕集合、模式及旋转匹配且排列通过验证时才恢复排列。不会创建缺席屏幕、启停驱动或安装 ICC 文件。
- 模式、DPI、HDR 和排列沿用各自的验证/失败恢复路径。场景是有序的多项操作，不是系统级原子事务；部分成功不会伪装成全部完成。
- “撤销上次切换”保存本次会话切换前的快照。撤销部分失败时保留快照以便重试；退出后不保留撤销历史。
- 重连/唤醒恢复亮度默认开启，只恢复在本应用调整过的设备。最多尝试三次；手动调整会取代旧恢复目标。更换端口造成设备 ID 变化时不会猜测映射。
- 自动恢复亮度不等于恢复完整场景。后者需在预设的更多选项中明确开启“连接这些屏幕时使用”，且屏幕集合必须完全匹配。

## 预设对比度 / Preset contrast

在支持 DDC/CI 对比度的屏幕上调整“对比度”，然后在“预设”中保存当前设置。切换预设会按设备 ID 恢复各屏保存的 0–100% 对比度；复制预设和“撤销上次切换”也保留对比度。已有预设需使用“用当前设置更新”重新捕获才会包含对比度；未记录对比度的预设不会修改该值。

Adjust **Contrast** on a display that exposes the DDC/CI control, then save the current settings under **Presets**. Applying the preset restores each display's saved 0–100% contrast by device ID. Duplicate preset and the existing **Undo last switch** snapshot preserve contrast too. Use **Update from current setup** to recapture an older preset if you want it to include contrast; presets without a saved contrast value leave it unchanged.

Contrast is restored after mode and HDR changes, using the refreshed display capabilities. If contrast is unavailable (including a monitor that locks it in HDR), invalid, or the DDC write fails, the preset reports a partial result and continues with brightness and other displays. This uses the existing DDC write path; an acknowledged write is not a guarantee that monitor firmware honored it. Automatic brightness restoration on reconnect remains brightness-only; contrast is restored when a full preset is applied.
## 捕获为草稿 / Capture a live setup as a draft

在“预设”页点击“捕获当前配置”，即可读取当前连接屏幕的设置，生成尚未保存的预设草稿。检查屏幕摘要、输入名称，然后点击“保存草稿”或“丢弃草稿”。保存使用捕获时的设置，不会再次读取或应用屏幕设置；之后调整屏幕或断开连接不会改变草稿。草稿仅在本次运行期间保留，切换页面不会丢失，退出应用后丢弃。一次只保留一份草稿；保存失败可重试，自动触发和快捷键默认关闭。

On **Presets**, click **Capture current setup** to read the connected displays into an **Unsaved preset**. Review the screen summary, edit the name, then **Save draft** or **Discard draft**. Capture reads the same settings supported by presets: brightness/dimming, output mode, rotation, scaling, readable HDR/ICC state, primary display and coordinates. Unavailable values remain unspecified. Capture, save and discard do not apply display settings. Saving uses the captured values without another scan, even after screens change or disconnect. Names must contain 1–40 characters and be unique, ignoring case and surrounding whitespace. One draft is kept in memory across page navigation and discarded on exit. Failed saves retain it for retry. Saved drafts start with automatic triggers and shortcuts off. No experimental flag is required.

## 复制预设 / Duplicate a preset

在“预设”页展开已有预设的“更多选项”，输入“新预设名称”，点击“复制预设”。名称为 1–40 个字符，不能与已有名称重复（不区分大小写）。副本拥有独立 ID，复制已保存的显示设置，无需连接原来的屏幕，也不会读取或应用当前设置。自动触发和快捷键默认关闭，可在副本的更多选项中单独配置；原预设及其自动化保持不变。

On the **Presets** page, expand **More options** for an existing preset, enter a **New preset name**, and click **Duplicate preset**. Names must contain 1–40 characters and be unique, ignoring case and surrounding whitespace. The copy gets its own ID and saved display settings, even if those screens are disconnected. Duplication does not capture or apply the current setup. Automatic triggers and the shortcut start off; configure them separately on the copy if needed. The original preset and its automation stay unchanged. No experimental flag is required.

## Experimental preset comparison

Disabled by default. Enable **Settings → Application → Preset comparison (experimental)**, then open a preset's **More options → Compare with current setup**. The persisted option is `ExperimentalPresetComparison` (default `false`). Disable the same toggle to remove the comparison controls.

Each click reads a fresh snapshot and shows **current → saved** differences for backlight brightness, software dimming, resolution/refresh rate, rotation, scaling, HDR, personal ICC scope/profiles, position and main display. It also lists disconnected preset displays and connected displays absent from the preset. Device IDs are matched without guessing by name. Older brightness-only presets compare only their saved fields.

The comparison does not apply settings, modify presets, or replace the undo snapshot. Unknown readings remain visible as **Unknown**. It is a snapshot, not a driver capability check or a guarantee of successful apply: layout restoration still requires matching screens and valid geometry, and unavailable ICC assignments are not installed. Click again after changing your setup to refresh the report. Applying the preset remains a separate action.

默认关闭。在 **设置 → 应用 → 预设比较（实验性）** 中启用，然后打开预设的 **更多选项 → 与当前设置比较**。每次点击读取当前快照，按“当前 → 已保存”显示亮度、调暗、模式、旋转、缩放、HDR/ICC、位置和主屏差异，并列出未连接或未包含的屏幕。旧预设只比较已保存的项目。无法读取的值显示为“未知”。比较不会应用设置、更改预设或替换撤销快照，也不保证驱动支持或排列能够恢复。更改设置后再次点击可刷新；关闭开关可移除比较控件。
## 撤销显示调整 / Undo a display adjustment

调整分辨率、刷新率、旋转或缩放后，在快捷面板的“预设”菜单或设置的“预设”页点击“撤销上次显示调整”。缩放撤销只恢复该屏幕的缩放；模式撤销恢复切换前的输出、缩放和排列，不改动亮度、软件调暗、HDR 或 ICC。与预设切换共用一个会话内撤销位置，下一次成功的显示调整或预设切换会替换它；无变化或已回滚的失败调整不会替换它。部分撤销失败时保留原快照供重试，退出应用后不保留。

After changing resolution, refresh rate, rotation or scaling, choose **Undo last display adjustment** in the quick panel's **Presets** menu or **Settings → Presets**. Scaling undo restores only that screen's scale. Mode undo restores the previous outputs, scaling and arrangement; brightness, software dimming, HDR and ICC remain as they are.

There is one undo slot per app session, shared with **Undo last switch** for presets. The next successful output/scaling adjustment or preset application replaces it. No-op changes and failed adjustments that roll back preserve it. An undo that partially fails retains the original snapshot for retry; reconnect missing displays before retrying. Layout restoration requires the same screen set and a valid extended layout, as with presets. This does not undo brightness, input, topology or layout-editor changes, and there is no redo or history across restarts.
## 应用到单个显示器 / Apply a preset to one display

在“预设”页展开“更多选项”，在“应用到单个显示器”中选择已连接的屏幕，再点击同名按钮。仅列出此预设已保存的设备，按设备 ID 匹配，不按名称或编号猜测。应用该屏幕的亮度、调暗、输出模式、旋转、DPI 和已保存的 HDR/ICC 设置；不恢复预设的排列或主屏。原预设不会被修改，“撤销上次切换”也仅恢复此次选择的屏幕。

Open **Presets → More options → Apply to one display**, choose a connected screen, then click **Apply to one display**. This is useful when you want one screen's saved setup while keeping the other screens' current settings. Only connected devices saved in that preset are offered; matching uses stable device IDs, not names or numbers. No experimental flag is required.

The action applies that screen's saved brightness, dimming, resolution, refresh rate, rotation, scaling, and HDR/ICC settings using the usual validation and recovery paths. Unspecified legacy settings stay untouched. Saved positions and the main-display choice are skipped, and the saved preset and its automation remain unchanged. Windows may still rearrange the desktop as a consequence of a resolution or rotation change. **Undo last switch** restores only the selected screen for this action and remains available after partial failure. Disconnection never redirects the operation to another screen.
## 排序预设 / Reorder presets

在“预设”页点击预设卡片上的“上移”或“下移”，即可将常用预设放在前面。顺序会自动保存，重启后保留，托盘预设菜单也使用同一顺序。排序不读取或应用屏幕设置，也不改变预设 ID、显示设置、快捷键或自动触发配置。多个自动化规则同时匹配时，仍按列表顺序选择第一个匹配项。

On the **Presets** page, use **Move up** or **Move down** on a preset card to put frequently used presets first. The order is saved immediately, survives restarts, and also appears in the tray preset menu. Reordering does not capture or apply display settings or change preset IDs, saved values, shortcuts, or trigger settings. Existing automation still selects the first matching preset in list order when multiple rules match. No experimental flag is required.
## 预设备注 / Preset notes

在“预设”页展开预设的“更多选项”，输入备注并点击“保存备注”。备注最多 500 个字符，支持多行文字，例如记录适用场景或连接提示。卡片显示备注预览，完整内容可在编辑框或悬停提示中查看。清空文字后保存即可删除备注。备注随预设保存，复制预设时一并复制；更新显示设置不会删除已有备注。保存备注无需连接屏幕，也不会读取或应用显示设置，更不会改变快捷键或自动触发配置。

Open **Presets → More options**, enter a **Note**, then click **Save note**. Use up to 500 characters across multiple lines to record when to use a setup or a connection reminder. The card shows a short preview; the editor and preview tooltip show the full note. Clear the text and save to remove it. Outer whitespace is trimmed. Notes survive restarts, copy with duplicated presets, and remain when updating the preset's display settings. Saving a note works with disconnected screens and does not capture or apply display settings or change shortcuts or automation. Failed saves keep your typed text for retry. No experimental flag is required.

## 搜索预设 / Search presets

在设置的“预设”页，在“按名称搜索预设”中输入名称的一部分即可即时筛选。搜索不区分大小写，会忽略首尾空白，并保留已保存的顺序。“清除搜索”恢复完整列表。搜索不会修改已保存的预设、应用显示设置或丢失卡片中尚未提交的编辑。筛选期间隐藏排序按钮，清除搜索后可继续排序。搜索词在本次会话中切换页面后保留，退出后不保存。

In **Settings → Presets**, type part of a name into **Search presets by name**. Results update as you type, ignore case and surrounding whitespace, and keep the saved order. The result count and an empty-result message help you narrow the list; **Clear search** restores all presets. Search only matches preset names and treats punctuation literally. Filtering preserves unsaved card edits and does not change saved presets or apply display settings. Reorder controls are hidden during a search; clear it to reorder. The query survives page navigation for this session and is not saved on exit. The tray menu continues to show all presets. No experimental flag is required.

## Reset one display to its preset

After successfully applying a preset, select a screen under **Settings → Displays** and use **Reset to preset**. Choose **Brightness**, **Scaling**, **Rotation**, or **All available**. The panel names the source preset. This is a normal manual control using existing display APIs; no experimental flag or new preference is introduced.

- Each display remembers its own last successfully applied preset values for the current app session. Failed or missing displays keep their earlier baseline; a layout-only failure does not discard successfully applied controls. Editing, renaming or deleting the saved preset does not change this snapshot. Applying another preset successfully replaces it. Undoing a preset switch does not replace it.
- Brightness restores the saved hardware brightness and software dimming directly for this display, bypassing group/sync targets and relative offsets, just as preset application does. An unspecified hardware brightness stays unchanged. Scaling restores the saved DPI percentage. Rotation preserves the current resolution and refresh rate, swapping width and height when changing between portrait and landscape.
- **All available** restores only these three controls where saved. It does not restore HDR, ICC, contrast, volume, inputs, output resolution, display arrangement or the primary screen. Rotation still changes the desktop footprint; Windows may adjust placement as part of the mode change. Legacy brightness-only presets leave scaling and rotation unavailable.
- Before a preset has been applied, or after restarting the app, the panel explains how to enable reset. No factory brightness or orientation is guessed. Reset does not consume or overwrite preset undo history and can be repeated. Missing devices and failed controls are reported in the status bar; hover over a partial result for details. Independent controls continue after a failure, and the baseline remains available for retry.
- Rotation and scaling reuse existing identity checks, driver validation and recovery paths. Workflow tests cover reset scope, legacy data, immutable snapshots, partial failures and disconnects; detached WPF fixtures cover action wiring. Real display writes still require Windows hardware validation.

在本次会话中成功应用预设后，打开“设置 → 屏幕”，使用“重置为预设”恢复该屏幕的亮度、缩放、旋转或所有可用项。仅重置所选屏幕；旧预设未保存的项目不可用。基准是上次成功应用的值，编辑或删除预设、撤销切换都不会改变它，退出应用后不保留。部分失败时可将鼠标悬停在状态栏查看详情并重试。

## 可选自动化

预设可设置每天的时间（本机 24 小时制）或接通电源/使用电池触发。应用运行期间约每 20 秒检查一次；休眠错过的最近一次时间可在恢复后触发，不补跑历史任务。同一轮多个预设命中时只应用列表中第一个匹配屏幕集合的预设。

在预设的“更多选项”中启用“每日计划”后，打开托盘面板即可看到“下次计划”：与当前连接屏幕集合完全匹配的下一个预设及本地日期和时间。同一时刻按预设列表顺序选择；当天计划时间已到则显示下一次未来计划。没有有效且匹配屏幕的每日计划时隐藏此行。面板打开期间约每 20 秒刷新，只更新提示，不修改或应用任何设置。

Enable **Daily schedule** under a preset's **More options**, then open the tray panel to see **Next scheduled** with the preset name and local date/time in your Windows regional format. Only schedules whose saved display set exactly matches the connected displays are considered; equal times use preset list order. At or after a scheduled time, the indicator advances to the next future occurrence, including tomorrow when needed. It hides when no valid, matching daily schedule exists and refreshes about every 20 seconds while the panel is visible. This read-only indicator needs no new setting or experimental flag. It shows the planned time, not a completion promise: sleep or a busy operation can delay application, and power/connection triggers are not predicted.

### 按星期运行（实验性） / Selected weekdays (experimental)

在“预设 → 更多选项”中启用时间触发，填写本机 24 小时制时间，再开启“按星期运行（实验性）”并选择星期。例如，办公预设可选周一至周五，周末预设可选周六和周日。此选项对每个预设默认关闭；已有每日计划保持原样。首次启用默认选择周一至周五，不选任何星期会暂停该预设的时间触发。关闭实验选项会恢复每日触发；要完全停用时间触发，请关闭时间旁的开关。电源和连接触发不受星期限制。

For separate workday and weekend display setups, open **Settings → Presets → More options**, enable the time trigger and enter a local **HH:mm** time. Turn on **Selected weekdays (experimental)** and choose the days for that named preset. Monday–Friday are initially selected. Configure another preset for Saturday–Sunday if desired.

- **Default OFF, per preset:** existing daily schedules keep their behavior. Enabling the weekday option alone does not enable the time trigger. Choosing no days suspends only that time trigger. Turning the experiment off returns an enabled time trigger to daily operation; use the time switch to stop it entirely.
- The app must be running. It polls about every 20 seconds and uses the computer's local clock. After sleep or a busy operation, only the latest daily occurrence within the past 24 hours is eligible, and its weekday must be selected. A Friday 23:59 occurrence checked just after midnight still belongs to Friday. Older selected days are never replayed. Starting the app does not catch up earlier events.
- Existing exact-screen matching and list-order priority still apply: only the first matching preset runs per poll. Connection and power triggers remain independent of the weekday selection. Manual preset application and **Undo last switch** still work normally.
- This is a local wall-clock schedule, not an exactly-once job service: nonexistent daylight-saving times can catch up after the clock jump; setting the clock backward may allow an occurrence to run again when its time is crossed again.

The saved preset fields are `ExperimentalWeeklySchedule` (default `false`) and `ScheduleDays` (local weekdays, serialized as 0 = Sunday through 6 = Saturday). Use the editor to configure them; duplicating a preset leaves its time trigger and experimental flag off.

设置、托盘、快捷键和命令行共用 0–100% 的亮度刻度。支持背光的屏幕在 20% 达到最低背光，继续向左拖动会用软件调暗。0% 仍能看到画面。没有背光接口的屏幕全程用软件调节。旧预设同时设置了背光和调暗时，调亮会先减弱调暗，调暗会先降低背光。

分组同步使用同一刻度，正偏移表示相对其他屏幕更亮，上下限限定各屏的目标百分比。亮度快捷键默认作用于鼠标所在屏幕，录入快捷键时临时停止全局按键处理。

托盘面板中的“联调亮度”用于临时多选：勾选的屏幕只接受亮度调整，不沿用组成员或相对偏移，仍遵守各屏亮度上下限。未选择屏幕时滑块不可用；退出联调恢复此前的单屏控制，不创建或修改显示器组。关闭面板后，下次默认进入单屏模式。

## 预设导入与导出 / Preset import and export

在“预设”页选择“导入预设”，或展开某个预设的更多选项并选择“导出预设”。每个 JSON 文件保存一个场景，可用于备份或在使用相同屏幕设备 ID 的 LumaDesk 安装之间迁移。

导入会新建预设，同名时自动添加编号，不覆盖现有预设，也不立即应用。快捷键、连接触发、定时和电源触发不会随文件转移；需要时请在导入后重新配置。文件保留屏幕 ID、名称及 ICC 配置文件引用，不包含 ICC 文件本身，不映射到其他屏幕，也不安装驱动或配置文件。分享前可检查 JSON 中的设备名称和配置文件引用。仅支持版本 1 的场景文件，最大 1 MiB；不能直接导入整个 `settings.json`。

In **Settings → Presets**, choose **Import preset…**, or expand a preset's additional options and choose **Export preset…**. Each JSON file contains one scene for backup or transfer between LumaDesk installations using the same display device IDs.

Import creates a separate preset and adds a number if its name is already used. It does not apply display changes. Shortcuts, connection triggers, schedules and power triggers are excluded; configure them again after import if needed. Device IDs, display names and ICC profile references are retained. ICC files are not bundled, displays are not remapped, and nothing is installed. Review the JSON's device names and profile references before sharing. The importer accepts version 1 scene files up to 1 MiB, not a complete `settings.json`. Missing displays or unavailable capabilities are reported by the existing scene apply flow when you choose to apply the preset.

## Experimental idle dimming

**Default: off.** Enable **Settings → Application → Idle dimming (experimental)** and choose 1–60 minutes, initially 5. The persisted settings are `ExperimentalIdleDimming` (boolean) and `IdleDimmingMinutes` (integer); old settings files keep the feature off. Invalid delays leave the experiment inactive.

While enabled, LumaDesk checks keyboard/mouse inactivity in the current Windows session every 500 ms using [GetLastInputInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo). After the delay, all connected screens get at least 60% software dimming; an existing darker shade stays as it is. Input or disabling the option removes the temporary shade. The brightness sliders, remembered reconnect values and captured presets retain their original values. This is visual dimming, not a backlight or energy-saving control.

Enabling, changing the delay, manual brightness/reset commands, display refresh/reconnection and waking from sleep start a fresh grace period. Dimming pauses during display scans, mode confirmation, scene application and brightness writes. Failed input sampling removes the temporary shade. Quitting closes the overlays. No polling timer runs for this feature when disabled.

There is no video/presentation detection: watching without keyboard/mouse input counts as idle, so turn the option off when needed. This experimental software overlay has the same HDR/full-screen compatibility limits as manual software dimming. It does not lock the session or replace Windows screen-off settings.

空闲调暗为默认关闭的实验性功能。在“设置 → 应用 → 空闲调暗（实验性）”开启，选择 1–60 分钟的等待时间（默认 5 分钟）。仅临时改变软件遮罩，键盘或鼠标活动会恢复原有亮度，不改写显示器背光、预设或重连恢复值。观看视频但不操作也会计入空闲，可按需关闭。

To validate on Windows: run `dotnet run --project tests/LumaDesk.Tests -c Release` for deterministic timing/settings tests and `dotnet run --project tests/LumaDesk.UiTests -- --fixtures` for settings UI tests. Manually enable a one-minute delay, test screens with 0%, 25% and 80% existing software dimming, then verify input, reset, disable, unplug/replug and sleep/wake restore correctly. Native input and actual overlay behavior require a Windows desktop.

## 实验性夜间时段 / Experimental night window

在“设置 → 应用 → 夜间时段（实验性）”中设置本地时间的开始和结束时刻（24 小时制 `HH:mm`），点击“应用时段”，再开启开关。默认关闭，建议时段为 `21:00–07:00`，支持跨午夜；开始和结束不能相同。应用运行期间，每个屏幕的软件调暗至少为 35%，更强的手动调暗会保留。它不改变硬件背光、亮度滑块、预设或记忆亮度。

启动、刷新屏幕后以及约每 20 秒检查当前时段。休眠或系统时间变化后按当前本地时间判断，不补跑错过的时段。结束时恢复当前手动调暗，期间的亮度调整不会被旧快照覆盖。关闭开关立即移除夜间调暗；“重置调暗”（默认 `Ctrl+Alt+0`）会清除调暗并暂停本次时段，直到下次每日时段、修改时段、关闭后重新开启或重启应用。这是按时间触发的临时遮罩，与空闲检测无关，也不会关闭显示器电源。

Enable **Settings → Application → Night window (experimental)** after choosing start/end times and clicking **Apply window**. Defaults off. See the [English usage notes](../README.en.md#experimental-night-window) for suspension and restoration behavior.

## 命令行

这是脚本接口；普通用户无需使用。所有命令要求结果文件，返回 JSON，成功退出码为 0，失败/部分完成为 1。

```powershell
$app = '.\LumaDesk.exe'
& $app --list --output .\screens.json
& $app --inventory --output .\inventory.json
& $app --brightness '1' 60 --output .\brightness-result.json
& $app --apply-preset '办公' --output .\preset-result.json
```

显示器参数可以是列表中的设备 ID、唯一名称或编号。长期脚本优先使用设备 ID；编号可能随连接变化。亮度命令遵循应用内分组和同步设置。预设可使用名称或 ID。

`--list-presets` 只读列出已保存的预设，按应用内顺序返回 `Data` 数组；每项含 `Id`、`Name` 和 `Displays`（已保存屏幕的 `Id`、`Name`）。未连接屏幕也会列出；没有预设时成功返回空数组。应用运行时读取内存中的预设，否则读取本地已保存的预设，不启动托盘应用或访问显示器。尚未保存、重复或不能作为参数使用的 ID 返回 `null`；脚本请输入非空 ID 给 `--apply-preset-id`，该命令不会按名称查找。

For scripts, `--list-presets` discovers saved preset IDs without changing any display settings. The JSON reply has `Success`, `Data`, and `Error`; `Data` contains `{ "Id", "Name", "Displays": [{ "Id", "Name" }] }` entries in library order, including disconnected displays. An empty library returns success with `Data: []`. It uses the running app's library when available, otherwise reads saved preferences without starting the app or probing monitors. Unsaved, duplicate, or unusable IDs are returned as `null`. Pass a non-null `Id` to `--apply-preset-id` for exact, case-sensitive matching without name fallback. The old `--apply-preset` still accepts a name or ID with its original matching behavior.

```powershell
$app = '.\LumaDesk.exe'
$result = Join-Path ([IO.Path]::GetTempPath()) ("lumadesk-presets-$([guid]::NewGuid().ToString('N')).json")

try {
    $run = Start-Process -FilePath (Resolve-Path -LiteralPath $app -ErrorAction Stop).Path `
        -ArgumentList ('--list-presets --output "{0}"' -f $result) -Wait -PassThru -ErrorAction Stop
    if ($run.ExitCode -ne 0) { throw "LumaDesk listing failed (exit $($run.ExitCode))." }

    $reply = Get-Content -LiteralPath $result -Raw -Encoding UTF8 -ErrorAction Stop |
        ConvertFrom-Json -ErrorAction Stop
    if ($reply.Success -ne $true) { throw 'LumaDesk did not return a successful preset list.' }
    $reply.Data | Where-Object { $null -ne $_.Id } | Select-Object Id, Name
} finally {
    if (Test-Path -LiteralPath $result) {
        Remove-Item -LiteralPath $result -ErrorAction SilentlyContinue
    }
}
```

枚举命令会等待查询完成后再读新的结果文件。确定要修改显示设置时，从列表复制所需的 ID，再用 `--apply-preset-id`；不默认选择第一项。修改命令可能启动长期运行的托盘进程，不要用示例中的 `Start-Process -Wait` 等待它。

The listing waits before reading a fresh result file. To change display settings, explicitly copy the desired ID from the list and pass it to `--apply-preset-id`; do not default to the first entry. Do not reuse the listing's `Start-Process -Wait` for an apply command, which may start the long-lived tray process.

已有应用时，命令通过限定当前 Windows 用户及桌面会话的本地命名管道交给应用执行；不会另建调暗覆盖层。未运行时，查询直接读取，修改命令启动托盘应用后再执行。发送命令后不自动重试，避免超时导致重复写入。管道不对网络开放。

## Diagnostic log export / 诊断日志导出

To collect logs for a support report without probing or changing displays:

```powershell
$process = Start-Process -FilePath '.\LumaDesk.exe' -ArgumentList '--export-log .\lumadesk-logs.zip' -Wait -PassThru
$process.ExitCode
```

`--export-log <archive.zip>` creates a new ZIP from the available `app.log` and `app.previous.log` in `%LOCALAPPDATA%\LumaDesk` (or `LUMADESK_DATA_DIR` when set). It works with the tray app running or closed and does not start or activate it. It rejects log entries that are reparse points, have multiple hard links, or cannot be verified as regular files. Use a trusted log directory and a trusted destination directory. The command does not separately collect settings, presets or recovery files. It copies logs verbatim, so review them for device names, local paths and exception details before sharing. While the app is running, logging and rotation may continue during the export.

The exit code is 0 on success or 1 for invalid arguments, no available logs, an invalid log entry, an existing destination or a read/write failure. The destination's parent directory must exist. The command finishes the ZIP before moving it to the requested name and never overwrites an existing destination. Export failures are not appended to the source logs. Unlike the automation commands above, this command takes no `--output` option and does not return JSON.

此命令将现有的当前和上一份日志导出为新的 ZIP，不探测或更改显示器，也不启动托盘窗口。拒绝重解析点（含符号链接）、有多个硬链接或无法验证为普通文件的日志。请使用可信的日志目录和输出目录。命令不会另外收集设置、预设或恢复文件，但会原样复制日志；分享前请检查其中的设备名称、路径和异常详情。ZIP 写完后才移动到指定位置，不覆盖已有文件。成功返回 0；参数无效、无日志、日志不合规、目标已存在或读写失败时返回 1。

### Connected-display inventory

`--inventory --output inventory.json` exports a fresh, read-only report of active desktop displays, including HDR support/enabled/active state, signal bit depth, and default SDR/HDR ICC profile names alongside identity, adapter, geometry, rotation (degrees), current/preferred pixel sizes, scaling and hardware control readings. Unlike `--list`, it includes color status and always queries locally, even when the tray app is running; it never starts the tray app or applies display settings.

The JSON uses the usual `Success` / `Data` / `Error` envelope. `Data` contains `SchemaVersion` (currently `1`), `CapturedAtUtc` (UTC scan start) and `Displays`. Unknown capability values are `null`, known unsupported/disabled HDR values are `false`, and a readable but unassigned ICC profile is `""`. Optional color-query failures retain the display with an unavailable reason; successful enumeration still exits with `0`. A scan or output-file failure exits with `1`. No active displays produces an empty array.

`HardwareBrightnessPercent` is the queried backlight level, not the combined software-dimming slider. Software dimming and user aliases are not included. Preferred pixel dimensions are `0` when unavailable. Device IDs identify the current connections and may change after moving ports. The report includes device IDs and profile names but excludes registry/WMI paths, settings, logs and ICC file contents. It covers active desktop displays, not disconnected or disabled outputs; mirrored outputs follow Windows monitor enumeration. Capture is sequential, so a hot-plug during capture can leave a display with unavailable color details. Like other JSON commands, the output file is overwritten if it exists.

## 硬件与性能边界

DDC 调用按设备串行，不同设备的探测及组合亮度同步可并行；实际延迟仍受显卡驱动和显示器限制。慢调用记录耗时。输入源能力仅在展开控件时读取，并缓存五分钟；列表来自能力串，不猜测不存在的 HDMI/DP 端口。参照 [Microsoft 低层显示器配置 API](https://learn.microsoft.com/en-us/windows/win32/monitor/using-the-low-level-monitor-configuration-functions)。输入切换成功返回后可能立即断开当前电脑，故不继续回读或自动重试。

画中画沿用 GDI SDR 捕获，在后台执行并复用图像缓冲；窗口最小化或暂停时停止捕获，不积压帧。可以拖选预览区域或返回完整画面。帧率选项是目标上限，取决于桌面大小和系统负载，不代表 HDR 或视频级低延迟捕获。

自动化测试使用模拟写入或真实只读枚举。DDC、HDR、模式切换、ICC、恢复与输入源的实际效果仍需硬件验证，见 [验证记录](validation.md)。
