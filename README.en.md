# LumaDesk

[简体中文](README.md) · English

An open-source Windows display manager with a compact Fluent-style tray panel.

![Quick panel](docs/images/quick-panel-en.png)

- Adjust combined hardware/software brightness and per-display DPI while dragging.
- Select a screen from its desktop layout, without visible numbers or display names. Locate briefly marks the physical screen. Brightness and scaling stay visible; More settings expands the other controls and stays expanded when switching screens.
- Link brightness lets you check multiple displays and adjust brightness only. Unchecked sync-group members are excluded; Done restores single-display controls. Selecting displays never changes their brightness.
- Manage resolution, refresh rate, orientation, HDR and ICC profiles in the app.
- Arrange displays, set the main display, extend or duplicate the desktop.
- Install the signed VirtualDrivers driver and manage virtual displays.
- Save screen setups (brightness, mode, scaling, readable HDR/ICC state and layout), apply them in one click, and undo the last switch.
- Sync brightness in groups; configurable shortcuts target the screen under the pointer by default and show a brightness indicator.
- Restore brightness after reconnect/wake for screens adjusted here. Optional scene schedules, power and connection triggers default off.
- Switch monitor-reported inputs, favorite output modes, rename/reorder screens, hide scaling controls and pin the panel.
- Input switching handles a detected digital-input firmware encoding mismatch and verifies readback. Ignored writes and unverified disconnections are reported separately.
- Follow the system light/dark theme. Picture in picture supports region selection and frame-rate choices with background capture.
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
Save and apply presets without configuring rules. Extra options stay collapsed.
See [workflow and command-line notes](docs/workflows.md) for automation, partial
results and the current-session undo behavior.

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
