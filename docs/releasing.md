# 发布 LumaDesk

每个 Windows x64 版本发布四个包。独立 EXE、带说明和许可的 portable ZIP，以及 MSI 均自带压缩的 .NET 运行时。轻量版 `LumaDesk-版本-slim-win-x64.exe` 使用本机已安装的 x64 .NET 8 桌面运行时。`SHA256SUMS.txt` 列出四个包的校验值。MSI 需要管理员权限，会创建开始菜单快捷方式，卸载时保留用户设置。应用与安装包目前未做代码签名。

## 发布步骤

1. 修改 `src/LumaDesk/LumaDesk.csproj` 的三段版本号，添加 `docs/releases/版本号.md`。
2. 在交互式 Windows 桌面运行 `./test.ps1`，确认 UI 回归通过；运行 `./package.ps1` 和 `./tools/Test-Package.ps1` 检查本地产物。
3. 提交并推送 `main`，等 Windows build 通过后，创建并推送对应的 `v版本号` 标签。

GitHub Actions 会核对标签和应用版本、运行离屏 WPF 测试、检查四项校验和，并分别启动两款 EXE。CI 还会检查 ZIP 和 MSI 元数据，在临时 runner 安装、启动和卸载 MSI。标记版本通过构建和离屏测试后，四个包及校验文件会上传到草稿 release，再公开为预览版。普通提交、PR 和手动运行只生成 Actions artifacts。

仅发布 job 拥有 `contents: write`，使用内置 `GITHUB_TOKEN`，无需额外 token。已公开 release 不会被重跑覆盖；失败留下的草稿可重跑补齐。MSI 的 `UpgradeCode` 保持不变，三段版本号递增以支持升级并阻止降级。

`./tools/Test-Package.ps1 -Install` 会安装及卸载应用，仅用于临时 Windows CI runner；检测到已有安装时拒绝继续。安装日志失败时保存在 Actions artifact 中。驱动和用户屏幕设置不参与打包测试。

## 更新 README 媒体

在 Windows 上使用 .NET 8 SDK、Python 和 Pillow 生成离屏截图及动图。

```powershell
dotnet run --project tests/LumaDesk.UiTests -c Release -- artifacts/readme-zh zh-Hans --demo
dotnet run --project tests/LumaDesk.UiTests -c Release -- artifacts/readme-en en --demo
python tools/make-readme-demo.py
```

快捷面板、设置总览和动图均使用实际 WPF 控件和同一组三块示例屏幕。`--demo` 在后台渲染控件，亮度操作只更新示例数据。每种语言的隔离目录包含截图及记录每帧时长和阶段的 `frames.tsv`。提交前检查中英文动图中的选屏、菜单、联调和主题切换。

构建依据：[GitHub 标签触发工作流](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow) · [WiX Package](https://docs.firegiant.com/wix/schema/wxs/package/) · [固定使用 WiX SDK 5.0.2](https://www.nuget.org/packages/WixToolset.Sdk/5.0.2)。
