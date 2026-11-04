param([switch]$Headless)
$ErrorActionPreference = 'Stop'
$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $sdk)) { $sdk = 'dotnet' }
& $sdk run --project (Join-Path $PSScriptRoot 'tests\LumaDesk.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Logic tests failed.' }
& $sdk run --project (Join-Path $PSScriptRoot 'tests\LumaDesk.ColorTests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Color tests failed.' }
& $sdk run --project (Join-Path $PSScriptRoot 'tests\LumaDesk.VirtualTests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Virtual display tests failed.' }
& $sdk run --project (Join-Path $PSScriptRoot 'tests\LumaDesk.LocalizationTests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Localization tests failed.' }
& $sdk run --project (Join-Path $PSScriptRoot 'tests\LumaDesk.WorkflowTests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Workflow tests failed.' }
if ($Headless) { return }
& $sdk run --project (Join-Path $PSScriptRoot 'tests\LumaDesk.UiTests') -c Release -- (Join-Path $PSScriptRoot 'artifacts\ui-tests')
if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
foreach ($language in @('en', 'zh-Hant', 'ja', 'ko')) {
    & $sdk run --project (Join-Path $PSScriptRoot 'tests\LumaDesk.UiTests') -c Release -- (Join-Path $PSScriptRoot "artifacts\ui-$language") $language
    if ($LASTEXITCODE -ne 0) { throw "UI localization tests failed: $language" }
}
