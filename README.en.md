# LumaDesk

[简体中文](README.md) · English

An open-source Windows display manager with a compact Fluent-style tray panel.

![Quick panel](docs/images/quick-panel-en.png)

- Adjust combined hardware/software brightness and per-display DPI while dragging.
- Manage resolution, refresh rate, orientation, HDR and ICC profiles in the app.
- Arrange displays, set the main display, extend or duplicate the desktop.
- Install the signed VirtualDrivers driver and manage virtual displays.
- Use picture in picture, brightness synchronization, presets and keyboard shortcuts.
- The quick panel stays anchored through DPI changes and closes when you click outside it.
- The monochrome halo tray icon follows the Windows taskbar theme.

Download the x64 portable ZIP from [Releases](https://github.com/Thweki/LumaDesk/releases), extract it and run `LumaDesk.exe`. The .NET runtime is included.

The interface follows the Windows display language by default. Simplified Chinese,
Traditional Chinese, English, Japanese and Korean are included. Other languages
fall back to English. Select an override under **Settings → Application → Display
language** and restart the app to apply it. Windows number and date formats are
kept independently of the UI language.

Click the tray icon for the quick panel, double-click for settings, or right-click
for the menu. Enable **Start with Windows** in Application settings if desired.

## Requirements and limitations

Windows 10/11 x64. Acrylic requires Windows 11 22H2 or later; HDR and ICC controls
depend on OS and driver capabilities. Driver installation and enable/disable,
and first-time system ICC installation, can request administrator approval.

HiDPI uses Windows per-display DPI. It is not macOS-style full-desktop 2× rendering
and downsampling. Some older applications can still look blurry. DDC response
speed depends on monitor firmware and connections. Hardware writes have not
been exhaustively validated on real devices; see [validation notes](docs/validation.md).

## Build

On Windows with the .NET 8 SDK:

```powershell
./build.ps1 -Portable
./test.ps1
```

Build output: `artifacts/LumaDesk-win-x64/`. Interactive WPF tests require a desktop
with active displays. CI runs the noninteractive checks and builds the portable app.
Tests do not send monitor setting changes.

Translations are embedded local JSON catalogs; no translation service is used at
runtime. See [localization notes](docs/localization.md) to add a language.

## License

Version 0.4.0 onward: [GPL-3.0-only](LICENSE). Previous releases through 0.3.1 retain
their original MIT license. Third-party components retain their licenses; see
[NOTICE](NOTICE). The corresponding source and build tools are included in each
release's matching Git tag.
