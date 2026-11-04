param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
[xml]$project = Get-Content (Join-Path $root 'src\LumaDesk\LumaDesk.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$release = Join-Path $root 'artifacts\release'
$prefix = "LumaDesk-$version"
foreach ($line in Get-Content (Join-Path $release 'SHA256SUMS.txt')) {
    $hash, $name = $line -split '  ', 2
    if ((Get-FileHash -LiteralPath (Join-Path $release $name)).Hash -ne $hash) { throw "Checksum mismatch: $name" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $release "$prefix-portable-win-x64.zip"))
try {
    foreach ($required in @('LumaDesk.exe', 'README.md', 'README.en.md', 'LICENSE', 'NOTICE', 'docs/images/quick-demo.gif', 'docs/images/quick-demo-en.gif')) {
        if (-not ($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $required })) { throw "Missing ZIP entry: $required" }
    }
} finally { $archive.Dispose() }
$msi = Join-Path $release "$prefix-win-x64.msi"
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msi, 0)
function Read-MsiProperty([string]$name) {
    $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$name'")
    [void]$view.Execute(); $record = $view.Fetch(); $value = $record.StringData(1); [void]$view.Close(); return $value
}
if ((Read-MsiProperty 'ProductVersion') -ne $version) { throw 'Incorrect MSI version.' }
if ((Read-MsiProperty 'UpgradeCode') -ne '{BB07ED3C-795F-4BCD-9D41-757B3542BFF1}') { throw 'MSI upgrade identity changed.' }
$productCode = Read-MsiProperty 'ProductCode'
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
if ($Install) {
    # Only use on a disposable CI runner; never replace an existing user installation.
    $target = Join-Path $env:ProgramFiles 'LumaDesk'
    if (Test-Path $target) { throw 'Installation already exists; refusing to replace it.' }
    $installAttempted = $false
    try {
        $installAttempted = $true
        $process = Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', '/norestart', '/l*v', "`"$release\install.log`"") -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin @(0, 3010)) { throw "MSI install failed: $($process.ExitCode)" }
        $installed = Join-Path $target 'LumaDesk.exe'
        if ((Get-FileHash $installed).Hash -ne (Get-FileHash (Join-Path $release "$prefix-win-x64.exe")).Hash) { throw 'Installed binary differs.' }
        $shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'LumaDesk.lnk'
        if (-not (Test-Path $shortcut)) { throw 'Start menu shortcut missing.' }
        # Invalid input is rejected before hardware access; verifies the installed runtime starts.
        $env:LUMADESK_DATA_DIR = Join-Path $release 'smoke-profile'
        $reply = Join-Path $release 'cli-smoke.json'
        $process = Start-Process $installed -ArgumentList @('--brightness', 'missing-screen', '101', '--output', "`"$reply`"") -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 1) { throw "Installed CLI smoke failed: $($process.ExitCode)" }
        if ((Get-Content $reply -Raw | ConvertFrom-Json).Success -ne $false) { throw 'Invalid brightness was not rejected.' }
    } finally {
        if ($installAttempted) {
            $process = Start-Process msiexec.exe -ArgumentList @('/x', $productCode, '/qn', '/norestart', '/l*v', "`"$release\uninstall.log`"") -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -notin @(0, 3010, 1605)) { throw "MSI uninstall failed: $($process.ExitCode)" }
        }
        Remove-Item Env:\LUMADESK_DATA_DIR -ErrorAction SilentlyContinue
    }
    if (Test-Path (Join-Path $target 'LumaDesk.exe')) { throw 'Uninstall left the application behind.' }
    if (Test-Path $shortcut) { throw 'Uninstall left the shortcut behind.' }
}
Write-Host 'Package checks passed.'
