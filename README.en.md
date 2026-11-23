# LumaDesk

[简体中文](README.md) · English

LumaDesk is an open-source display manager for Windows. Click the system tray icon, pick a monitor by its position on your desk, and adjust its brightness or scaling. Save brightness, resolution and display layout together as a preset to restore the next time you connect those screens.

**[Download LumaDesk 0.6.0](https://github.com/Thweki/LumaDesk/releases/tag/v0.6.0)** · Windows 10 / 11 x64

![Selecting a monitor by position, then adjusting brightness across several screens](docs/images/quick-demo-en.gif)

## Which download should I choose?

| If you want to… | Download | How to use it |
| --- | --- | --- |
| Run the app without installing it | [Standalone EXE](https://github.com/Thweki/LumaDesk/releases/download/v0.6.0/LumaDesk-0.6.0-win-x64.exe) | Double-click to run. The .NET runtime is bundled. |
| Use the x64 .NET 8 Desktop Runtime already on your PC | [Slim EXE](https://github.com/Thweki/LumaDesk/releases/download/v0.6.0/LumaDesk-0.6.0-slim-win-x64.exe) | Choose this for the smallest download. Double-click to run. |
| Keep the app and its documentation together | [Portable ZIP](https://github.com/Thweki/LumaDesk/releases/download/v0.6.0/LumaDesk-0.6.0-portable-win-x64.zip) | Extract the whole folder, then run `LumaDesk.exe`. |
| Launch from Start or uninstall through Windows Settings | [MSI installer](https://github.com/Thweki/LumaDesk/releases/download/v0.6.0/LumaDesk-0.6.0-win-x64.msi) | Run the installer with administrator approval. |

All four editions share settings and presets in `%LOCALAPPDATA%\LumaDesk`. If you use the earlier 0.6.0 build, download your edition again to get the automatic updates switch. The release includes [SHA-256 checksums](https://github.com/Thweki/LumaDesk/releases/download/v0.6.0/SHA256SUMS.txt) for all four files.

## First steps

1. **Adjust one screen.** Click the tray icon, select the monitor on the display map, and drag the brightness slider. To pick a monitor by name, open **⋯ → Show display list**.
2. **Adjust several screens together.** Open **⋯ → Link brightness**, select the screens, then drag the slider. Each selected screen takes the same target brightness.
3. **Save a setup.** Double-click the tray icon to open settings and choose **Presets → Save current settings**. For example, save a daytime setup and an evening setup for the same pair of monitors, then switch between them from the quick panel. [Presets and automation](docs/workflows.md)

By default, `Ctrl+Alt+↑` and `Ctrl+Alt+↓` adjust brightness on the screen under the pointer. `Ctrl+Alt+0` clears software dimming on all screens.

## Startup and automatic updates

Under **Settings → Application**, you can enable either option independently.

- **Start with Windows** launches LumaDesk in the system tray when the current user signs in.
- **Automatic updates is off by default.** When enabled, LumaDesk checks GitHub for new releases, including previews, and checks again daily while running. Once the download passes SHA-256 verification, right-click the tray icon and choose **Exit LumaDesk** to install it. Turning the option off before exiting cancels the pending update. MSI upgrades may ask for administrator approval.

The same page lets you choose a light or dark theme and switch between English, Simplified Chinese, Traditional Chinese, Japanese and Korean.

## Display controls

| Task | What you can do |
| --- | --- |
| Arrange your desktop | Drag screens into place, choose the primary display, and extend or duplicate the desktop. For precise alignment, position one screen relative to another. [Display layout](docs/display-layout.md) |
| Change picture settings or text size | Set each display’s resolution, refresh rate, rotation and Windows scaling. HDR and ICC controls depend on Windows, your graphics driver and the display. [HDR and color management](docs/color-hdr.md) |
| Control an external monitor | Adjust backlight brightness, contrast and monitor volume, or switch inputs over DDC/CI. Available controls vary by monitor and connection. [Hardware validation notes](docs/validation.md) |
| Add virtual displays | Install the optional VirtualDrivers driver from the app, then choose how many virtual screens to create. The initial installation requires administrator approval. [Virtual displays](docs/virtual-displays.md) |

### Experimental night window

To dim your screens on a schedule, open **Settings → Application → Night window (experimental)**, enter local start and end times, select **Apply window**, and enable the switch. It starts off and supports overnight schedules. While active, LumaDesk applies at least 35% software shading and preserves any stronger manual shading. Your manual shading level returns when the window ends. Press `Ctrl+Alt+0` to clear shading early and suspend the current window.

## Build from source

Install the .NET 8 SDK on Windows and run the commands you need from the repository root.

```powershell
.\build.ps1 -Portable   # Build a standalone EXE with the runtime bundled
.\build.ps1 -Slim       # Build the slim EXE
.\test.ps1 -Headless    # Run the headless test suites
.\package.ps1           # Write all four packages and checksums to artifacts/release/
```

When reporting a hardware issue, include the monitor model, how it is connected, what you tried and what happened. [Report an issue](https://github.com/Thweki/LumaDesk/issues) · [Build status](https://github.com/Thweki/LumaDesk/actions/workflows/build.yml) · [Release instructions](docs/releasing.md)

LumaDesk is licensed under GPL-3.0-only from version 0.4.0 onward. Versions through 0.3.1 retain their MIT license. Third-party components have their own licenses. [LICENSE](LICENSE) · [NOTICE](NOTICE)
