# 应用内色彩与 HDR 控制

实现日期：2026-11-02。新增 `AdvancedColorService.cs` 和 `MainWindow.Color.cs`；不依赖打开 Windows 设置完成这些操作。

## 已接入的操作

- 每屏读取 HDR 能力、用户开关、当前活动颜色模式、位深和输出编码；真实切换 HDR，随后回读用户开关。用户已启用而系统尚未激活 HDR 时分别显示，避免把两种状态混为一谈。
- 读取当前屏幕的 SDR 与 HDR 默认 ICC，以及系统已安装的显示器 RGB 配置。可分别设置 SDR/HDR 默认、在应用内选文件安装并应用、恢复该屏幕的系统配置。修改默认前开启该物理显示器的当前用户配置作用域。
- HDR 活动时读取 SDR 白色亮度的尼特值；没有找到本次采用的正式 CCD 接口中的写入命令，因此没有把未公开调节接口包装成受支持的滑块。

这些接口用于 Windows 显示配置，不包含 Apple XDR、任意颜色效果、3D LUT、硬件校准或其他平台的合成管线。

## 原生接口依据

| 范围 | 实现及来源 |
| --- | --- |
| 屏幕映射 | `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` 与 source GDI 名称匹配；HDR 用 target adapter/id，ICC 用 target adapter/source id。操作前核验实际设备实例；同源镜像无法唯一定位时拒绝单屏写入。 |
| HDR 与 WCG 区分 | SDK 的 `GET_ADVANCED_COLOR_INFO_2` 类型 15 明确提供 HDR 支持位、用户启用位及活动模式；`SET_HDR_STATE` 类型 16 专门切 HDR。[Microsoft 设备信息类型](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_device_info_type)、[Microsoft WinSDK wingdi.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/wingdi.h) |
| 旧系统回退 | 新结构查询失败时只读 `GET_ADVANCED_COLOR_INFO` 的通用颜色信息；不将旧 `advancedColorEnabled` 当作 HDR，也不调用可能切换 WCG 的旧 setter。此路径显示 HDR 开关不可用，不跳出应用。 |
| SDR 白色亮度 | 只读 `GET_SDR_WHITE_LEVEL`；值乘 0.08 转换为尼特。[Microsoft SDR_WHITE_LEVEL](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_sdr_white_level) |
| 默认配置 | 使用 [ColorProfileGetDisplayDefault](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-colorprofilegetdisplaydefault) 与 [ColorProfileSetDisplayDefaultAssociation](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-colorprofilesetdisplaydefaultassociation)。SDR subtype=7，HDR subtype=8，不用 legacy WCS getter 读取 HDR 默认。[高级颜色下 ICC 行为](https://learn.microsoft.com/en-us/windows/win32/wcs/advanced-color-icc-profiles) |
| 列表与关联 | [ColorProfileGetDisplayList](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-colorprofilegetdisplaylist) 返回此屏幕关联列表；[ColorProfileAddDisplayAssociation](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-colorprofileadddisplayassociation) 创建指定作用域/SDR 或 HDR 关联。系统色彩目录另列可选的已安装显示器 RGB 文件；这部分是全系统候选，不声称均为本屏校准文件。 |
| 安装与用户作用域 | [InstallColorProfileW](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-installcolorprofilew) 安装指定文件；[ColorProfileGetDisplayUserScope](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-colorprofilegetdisplayuserscope) 读取当前作用域；[WcsSetUsePerUserProfiles](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-wcssetuseperuserprofiles) 选择当前用户配置，首次启用会复制原系统关联。枚举值来自 [Microsoft Icm.h](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/Icm.h)。 |

结构体使用 Windows 固定宽度 `UINT32`：CCD header=20 字节，Info2=36 字节（flags 偏移20，activeMode 偏移32），旧 Info=32 字节，HDR/SDR-white value packet=24 字节。没有采用把 Windows `UINT32` 错映射为主机 64 位值的第三方结构布局。

## 失败处理与边界

- HDR 写入检查 API 返回及后续开关回读；未确认时报告未确认，不伪报成功。用户开关成功不保证系统策略允许 HDR 立即激活。
- ICC 切换回读所选屏幕的作用域和 SDR/HDR 默认。失败时尝试恢复旧默认或原系统作用域。已经安装的文件和新增关联可能保留；不是系统级原子事务。
- 安装前检查文件头、RGB 显示器类别及长度；同名但不同内容的系统文件拒绝覆盖。正式安装由 Windows 验证。仅新配置的安装返回 `ERROR_ACCESS_DENIED` 时，应用显示“需要 Windows 系统授权”并请求 UAC；取消授权不应用配置。其他错误直接报告。
- 恢复系统配置同时影响该屏幕的 SDR 与 HDR 选择；应用界面已说明，不改变其他屏幕的作用域。
- 现代 ColorProfile API 不存在时禁用对应控制并显示原因；不是所有 Windows 10 构建都提供这些接口。HDR info2 不可用的系统暂只读颜色信息。
- WCS 的设备名称使用 `EnumDisplayDevices` 获取的物理显示器 `DeviceKey`，并核对其作用域与 CCD 结果一致。本机只读探测发现 `\\.\DISPLAY1` 虽返回成功，却返回了与真实屏幕不同的作用域，故不作为 setter 的候选。
- ICC 管理不承诺所有应用正确采用配置，也不等于测量、生成或验证显示器的校准精度。

## 验证

在仓库根目录执行：`.\.tools\dotnet\dotnet.exe run --project tests/LumaDesk.ColorTests -c Release -- --probe`。

17 项检查通过，涵盖原生结构尺寸/偏移、WCG 与 HDR 区分、开关与活动状态区分、策略位、配置文件类别/截断检查、无效输入拒绝及固定安装命令参数。测试只创建并删除临时 ICC 头文件；畸形文件在进入原生安装前被拒绝。不安装、不关联、不启动 UAC，不调用 HDR 或颜色 setter。

只读实机结果：

| 屏幕 | HDR 能力 / 开关 / 活动模式 | 编码 | 配置读取 |
| --- | --- | --- | --- |
| ANT272ZQE | 支持 / 关闭 / 广色域 SDR | RGB，10 位/通道 | 当前用户作用域，读到已有 HDR 校准配置 |
| H25T7 | 支持 / 关闭 / SDR | RGB，10 位/通道 | 系统作用域 |
| F27G30Q Pro | 支持 / 关闭 / SDR | RGB，10 位/通道 | 系统作用域 |

没有实际切换 HDR、安装 ICC 或更改任何屏幕的默认配置。跨版本、镜像、多 GPU、受策略限制及真实写入后的显示效果仍未实机验证。

## 集成

主设置页或快捷面板调用 `CreateColorControls(display, compact: true/false)`，返回 `UIElement`。控件加载时异步读取；操作在任务中执行，局部禁用并回读状态，错误在当前卡片显示。无需修改 `DisplayInfo` 或增加持久化状态；状态由系统读取。

## Windows 安装权限

微软区分系统级安装与个人配置：颜色文件安装需要管理员权限，当前用户的关联和默认设置可在普通用户上下文完成。[Microsoft Profile Management Functions](https://learn.microsoft.com/en-us/windows/win32/wcs/profile-management-functions)

应用的 `--install-color-profile <文件>` 固定辅助入口只调用 `InstallProfileOnly`，再次验证 RGB 显示器 ICC、同名碰撞并安装；不接受任意命令，不调整系统信任，不设置显示器默认。正常进程仅在原生安装返回拒绝访问后使用本应用可执行文件的 `runas` 启动它，文件路径通过 `ArgumentList` 独立传递。完成后重新核对系统中安装文件的 SHA-256，再在**原用户进程**设置所选屏幕的默认配置。因此 UAC 使用另一管理员账户也不会把默认配置写入那个账户。

已存在且内容一致的系统配置可直接应用，通常不需要再授权。未实测 UAC 同意/取消和真实安装；参数与无效输入路径通过无写入测试。权限请求由 Windows 显示，不需要进入 Windows 设置页面。
