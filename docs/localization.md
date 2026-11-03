# Language resources / 多语言

LumaDesk embeds five JSON catalogs: `zh-Hans`, `zh-Hant`, `en`, `ja`, and `ko`.
All 417 keys are present in each catalog, including dialogs, errors and tray menus.

On a fresh start, `auto` reads the current Windows display language via .NET's
`CurrentUICulture`. Chinese script/region variants resolve to Hans or Hant;
Japanese and Korean regional variants resolve to their language. Unsupported
languages fall back to English. An explicit selection is saved to settings and
takes effect at the next app start. `CurrentCulture` is not overwritten, so
Windows regional number and date formatting remain independent.

WPF windows receive a matching language tag and Windows UI font fallback:
Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI or Malgun Gothic alongside
Segoe UI. Native Windows error messages and hardware-provided names are not
rewritten by the application.

To add or revise translations, keep resource IDs and format arguments unchanged.
For example, `{0}`, `{1:HH:mm}` and `{0:X8}` must survive translation. The catalogs
use regular JSON strings; line breaks are encoded as `\n`. Do not translate device
IDs, protocol commands or persisted navigation/language identifiers.

Run `tests/LumaDesk.LocalizationTests` to check locale resolution, completeness,
format arguments, English fallback and background-thread language propagation.
Run `test.ps1` on an interactive Windows desktop for localized UI checks and
screenshots in `artifacts/ui-en`, `ui-zh-Hant`, `ui-ja`, and `ui-ko`.

Catalog translations are maintained in the repository and licensed under
GPL-3.0-only. No online translation API is contacted by the app.
