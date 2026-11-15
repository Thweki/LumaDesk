# 屏幕排列

支持拖动预览、边缘吸附、输出像素坐标、主屏选择、横向/纵向排列，以及扩展/复制拓扑。预览不写系统；点击应用后才提交。

精确相邻放置：先选择要移动的屏幕，再选择参考屏幕、左侧/右侧/上方/下方以及边缘或居中对齐，点击「在预览中放置」。左右放置支持顶部/居中/底部对齐，上下放置支持左侧/居中/右侧对齐。仅移动所选屏幕；其他屏幕与主屏标识保持不变。最后点击应用，验证完整排列后再提交。与第三块屏幕重叠或断开的预览仍会被拒绝。

### Precise relative placement

In the display layout editor, select the screen to move, then choose a neighbor under **Position selected display relative to**. Choose **Left of**, **Right of**, **Above**, or **Below**, and an edge or center alignment. Click **Place in preview**, then **Apply** when the full layout is ready.

This closes the gap to that neighbor using output pixels, including for mixed portrait/landscape screens. Left/right placement aligns top edges, centers, or bottom edges; above/below placement aligns left edges, centers, or right edges. Half-pixel center offsets round toward the top/left. Other screens and the primary designation stay unchanged. Placement only edits the preview; Apply still rejects overlaps or disconnected screens and uses the existing rollback-protected layout transaction. At least two displays are required.

坐标取自 EnumDisplaySettings 的原生模式，与分辨率使用相同物理像素单位。主屏位置归零，其余屏幕整体平移，保留相对关系。纯规则拒绝重叠、断开、重复设备及多个主屏。

读取完整 CCD 路径和 64 字节模式联合体，仅修改源位置，先使用 SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG 验证整套排列，再整体应用保存。不能逐屏单独验证主屏迁移：其他屏幕未移动时会误判完整布局。

应用后回读位置、尺寸和主屏标识；失败恢复原模式与排列，另有超时恢复进程。设备身份变化时拒绝写入替代设备。共享源需先切换扩展才能独立排列。

本次执行了三屏替换主屏后的完整 SDC_VALIDATE，未真正移动桌面。实际应用、失败恢复、热插拔和重启持久化仍需实机覆盖。

来源：[SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)、[源模式与主屏定义](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_source_mode)、[多屏批量恢复](https://devblogs.microsoft.com/oldnewthing/20211222-00/?p=106048)。
