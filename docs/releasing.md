# 发布 LumaDesk

发行文件均为 Windows x64，包含 .NET 运行时：独立 EXE、带说明和许可的 portable ZIP、安装到 Program Files 的 MSI，以及 `SHA256SUMS.txt`。MSI 需要管理员权限，创建开始菜单快捷方式；卸载保留用户设置。应用与安装包目前未做代码签名。

## 发布步骤

1. 修改 `src/LumaDesk/LumaDesk.csproj` 的三段版本号，添加 `docs/releases/版本号.md`。
2. 在交互式 Windows 桌面运行 `./test.ps1`，确认 UI 回归通过；运行 `./package.ps1` 和 `./tools/Test-Package.ps1` 检查本地产物。
3. 提交并推送 `main`，等 Windows build 通过后，创建并推送对应的 `v版本号` 标签。

GitHub Actions 会重新测试和构建，核对标签与应用版本，检查 ZIP、校验和及 MSI 元数据，在临时 runner 安装、启动和卸载 MSI。所有检查成功后才将三个包和校验文件上传到草稿 release，再公开为预览版。普通提交、PR 和手动运行只生成 Actions artifacts。

仅发布 job 拥有 `contents: write`，使用内置 `GITHUB_TOKEN`，无需额外 token。已公开 release 不会被重跑覆盖；失败留下的草稿可重跑补齐。MSI 的 `UpgradeCode` 保持不变，三段版本号递增以支持升级并阻止降级。

`./tools/Test-Package.ps1 -Install` 会安装及卸载应用，仅用于临时 Windows CI runner；检测到已有安装时拒绝继续。安装日志失败时保存在 Actions artifact 中。驱动和用户屏幕设置不参与打包测试。

## 更新 README 媒体

需要交互式 Windows 桌面、.NET 8 SDK，以及 Python + Pillow：

```powershell
dotnet run --project tests/LumaDesk.UiTests -c Release -- artifacts/readme-zh zh-Hans --demo
dotnet run --project tests/LumaDesk.UiTests -c Release -- artifacts/readme-en en --demo
python tools/make-readme-demo.py
```

动图使用生产 WPF 控件、示例显示器和注入的亮度回调；设置和截图输出到隔离目录，不发送硬件调整命令。`frames.tsv` 记录每帧时长和演示阶段。静态快捷面板截图也使用示例屏幕，完整设置截图来自本机只读枚举。提交前检查中英文图片和 GIF，避免界面变更后留下旧截图。

构建依据：[GitHub 标签触发工作流](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow) · [WiX Package](https://docs.firegiant.com/wix/schema/wxs/package/) · [固定使用 WiX SDK 5.0.2](https://www.nuget.org/packages/WixToolset.Sdk/5.0.2)。
