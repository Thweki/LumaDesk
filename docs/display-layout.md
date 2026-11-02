# 屏幕排列

支持拖动预览、边缘吸附、输出像素坐标、主屏选择、横向/纵向排列，以及扩展/复制拓扑。预览不写系统；点击应用后才提交。

坐标取自 EnumDisplaySettings 的原生模式，与分辨率使用相同物理像素单位。主屏位置归零，其余屏幕整体平移，保留相对关系。纯规则拒绝重叠、断开、重复设备及多个主屏。

读取完整 CCD 路径和 64 字节模式联合体，仅修改源位置，先使用 SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG 验证整套排列，再整体应用保存。不能逐屏单独验证主屏迁移：其他屏幕未移动时会误判完整布局。

应用后回读位置、尺寸和主屏标识；失败恢复原模式与排列，另有超时恢复进程。设备身份变化时拒绝写入替代设备。共享源需先切换扩展才能独立排列。

本次执行了三屏替换主屏后的完整 SDC_VALIDATE，未真正移动桌面。实际应用、失败恢复、热插拔和重启持久化仍需实机覆盖。

来源：[SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)、[源模式与主屏定义](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_source_mode)、[多屏批量恢复](https://devblogs.microsoft.com/oldnewthing/20211222-00/?p=106048)。
