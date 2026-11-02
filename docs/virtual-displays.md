# 虚拟屏幕实现与验证

2026-11-02。此功能在 LumaDesk Windows 应用内提供驱动安装、启用/暂停、创建及减少虚拟屏幕数量。它使用第三方签名的 VirtualDrivers / MikeTheTech Virtual Display Driver，不是自行签发的驱动，也没有将普通窗口当成虚拟显示器。

## 应用内工作流

1. 打开「虚拟屏幕」。应用查询设备的硬件 ID 和真实显示适配器，并探测驱动命名管道。
2. 未安装时，点击「下载并安装虚拟显示驱动」。应用下载固定官方版本，检查发布包与四个文件的 SHA-256，并由 Windows 验证 DLL 和 catalog 的 Authenticode 签名。通过后才请求 UAC。
3. 提权后的同一程序再次检查固定文件哈希，将已验证字节复制到保护目录，注册 `Root\MttVDD` 并通过 Windows SetupAPI / NewDev 安装签名 INF。不会导入信任证书、启用测试签名或关闭 Secure Boot。
4. 安装后暂停驱动。选择 1–8 台并应用，会启用驱动并设置数量；之后可在应用的设备控制和排列窗口管理新屏幕。启用或暂停驱动需要 UAC；已启用时更改数量使用驱动管道。
5. 设为 0 会真正停用对应硬件 ID 的驱动，移除其屏幕，保留驱动供再次启用。不会卸载其他显卡。

上游 25.7.23 源码会将配置里的 `count=0` 强制改为 1，因此不能用 `SETDISPLAYCOUNT 0` 表示移除最后一台；本实现专门用设备停用路径处理零台。驱动加载/重载期间可能短暂出现一台屏幕，Windows 可能重新排列窗口。安装失败或需重启时，界面保留实际查询结果，不假报创建成功。

## 固定供应链

| 项目 | 值 |
| --- | --- |
| 官方项目 | [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) |
| 发布版 | [25.7.23](https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/tag/25.7.23) |
| 下载资产 | `VirtualDisplayDriver-x86.Driver.Only.zip`，132,118 字节；名称含 x86，但 INF 声明 `Standard.NTamd64`，这里仅安装 x64 |
| 包 SHA-256 | `e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a` |
| catalog SHA-256 | `08a0093fc9b2e32b287a6f8a77ca4de0a31830d29fc33d2b13a918dc859468f6` |
| DLL SHA-256 | `c9ca837f57a98fbd43bc416a7f535a95843626e7759eaf85cf0cd7ce334dbb05` |
| INF SHA-256 | `550d211fe481e74dfe3f9d724ed78be48b3a9113405965d683d9373e8d672f5d` |
| 配置模板 SHA-256 | `edb2501d6d5da17f66d15d4b97a6f4a3f0d8963165ac4a6a6259d95118288020` |
| 本机签名检查 | catalog、DLL 均为 Valid，签名者 SignPath Foundation；由本程序 WinVerifyTrust 重新检查通过 |

应用不自动更新到未经本项目复核的驱动版本。原发布包按需下载，不随主程序重复捆绑；上游 [MIT 许可证](third-party/Virtual-Display-Driver-LICENSE.txt)保留在发行文档中。

## 协议与设备验证

协议直接依据 [25.7.23 Driver.cpp](https://github.com/VirtualDrivers/Virtual-Display-Driver/blob/25.7.23/Virtual%20Display%20Driver%20(HDR)/MttVDD/Driver.cpp)：`HandleClient` 读取一次 UTF-16LE 命令后断开管道；`PING` 的 `PONG` 和日志回复使用窄字符字节。客户端使用 `MTTVirtualDisplayPipe`、带 NUL 的 UTF-16LE 请求和读到断开的有界回复，并设置连接/操作超时。没有发送独立 `RELOAD_DRIVER` 命令。

`SETDISPLAYCOUNT` 会写配置并重载适配器。应用不会把“写入管道成功”视为“创建成功”：必须同时看到保存的数量和真实活动虚拟适配器数量相符。暂停后必须看到设备禁用状态及活动数量归零。设备识别使用 `Root\MttVDD` **硬件 ID**，不依赖设备实例路径必须长成 `ROOT\MTTVDD\...`；其他安装器可能创建 `ROOT\DISPLAY\...`。

安装过程使用的设备注册与状态变更接口依据 [SetupDiCreateDeviceInfo](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdicreatedeviceinfow)、[UpdateDriverForPlugAndPlayDevices](https://learn.microsoft.com/en-us/windows/win32/api/newdev/nf-newdev-updatedriverforplugandplaydevicesw) 和 [SP_PROPCHANGE_PARAMS](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/ns-setupapi-sp_propchange_params)。驱动配置路径通过其 `HKLM\SOFTWARE\MikeTheTech\VirtualDisplayDriver\VDDPATH` 约定写入应用专用 ProgramData 子目录。

## 验证与限制

独立测试：

```powershell
.\.tools\dotnet\dotnet.exe run --project tests/LumaDesk.VirtualTests -c Release
# 加上真实官方下载与 Windows 签名检查；仍不安装驱动
.\.tools\dotnet\dotnet.exe run --project tests/LumaDesk.VirtualTests -c Release -- --verify-download
```

本机 16 项检查通过，含原生结构尺寸、数量边界、XML/DTD 防护、哈希篡改拒绝、真实本地命名管道的字节编码/断开/取消、官方包下载与 WinVerifyTrust。当前机器只读结果为 `Installed=false`、活动虚拟屏幕 0。**本轮未执行驱动安装、停用、启用或创建屏幕，因此这些写入路径仍待用户在应用内安装后的实机验证。**

当前仅支持 x64 和上述驱动系列；系统需已有 Microsoft Visual C++ x64 运行库。不同虚拟驱动不会被误认为兼容。数量上限 8 是本应用的边界，并非声明上游硬件极限。此功能不包含任意虚拟模式写入、独立卸载驱动或全桌面串流；驱动公布的分辨率/刷新率可通过已有显示模式控件选择。创建虚拟显示器本身不等于复制 macOS 的 2× 渲染/降采样链路。
