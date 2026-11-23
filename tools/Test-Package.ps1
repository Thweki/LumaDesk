param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
[xml]$project = Get-Content (Join-Path $root 'src\LumaDesk\LumaDesk.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$release = Join-Path $root 'artifacts\release'
$prefix = "LumaDesk-$version"
& (Join-Path $PSScriptRoot 'Test-ReleaseChecksums.ps1') -ReleaseDirectory $release -Version $version
function Test-ExeStartup([string]$Path) {
    # An out-of-range value exercises the published host and WPF startup, then exits before any display access.
    $profile = Join-Path $root 'artifacts\package-smoke'
    [void][IO.Directory]::CreateDirectory($profile)
    $reply = Join-Path $profile ("result-{0}.json" -f [guid]::NewGuid())
    $start = [Diagnostics.ProcessStartInfo]::new($Path, "--brightness missing-screen 101 --output `"$reply`"")
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = 'Hidden'
    $start.EnvironmentVariables['LUMADESK_DATA_DIR'] = $profile
    $start.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS'] = '1'
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw "Startup timed out: $Path" }
        if ($process.ExitCode -ne 1) { throw "Unexpected startup exit code $($process.ExitCode): $Path" }
        $result = Get-Content -LiteralPath $reply -Raw | ConvertFrom-Json
        if ($result.Success -ne $false -or $result.Error -ne 'Invalid display command.') { throw "Unexpected CLI reply: $Path" }
    } finally { $process.Dispose() }
}
$standalone = Get-Item -LiteralPath (Join-Path $release "$prefix-win-x64.exe")
$slim = Get-Item -LiteralPath (Join-Path $release "$prefix-slim-win-x64.exe")
if ($slim.Length -ge $standalone.Length) { throw 'The slim EXE must be smaller than the self-contained EXE.' }
Test-ExeStartup $standalone.FullName
Test-ExeStartup $slim.FullName
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
        Test-ExeStartup $installed

        # Exercise the installed log-export CLI using only isolated, synthetic files.
        $logSmoke = Join-Path ([IO.Path]::GetTempPath()) ("lumadesk-log-smoke-" + [Guid]::NewGuid().ToString('N'))
        $data = Join-Path $logSmoke 'test data'
        $zipPath = Join-Path $logSmoke 'support logs.zip'
        $previousDataDir = $env:LUMADESK_DATA_DIR
        try {
            [void][IO.Directory]::CreateDirectory($data)
            $env:LUMADESK_DATA_DIR = $data
            $expectedLogs = @{
                'app.log' = [Text.Encoding]::UTF8.GetBytes("Current synthetic log $([char]0x2603)`r`n")
                'app.previous.log' = [Text.Encoding]::UTF8.GetBytes("Previous synthetic log`n")
            }
            foreach ($name in $expectedLogs.Keys) {
                [IO.File]::WriteAllBytes((Join-Path $data $name), $expectedLogs[$name])
            }
            [IO.File]::WriteAllText((Join-Path $data 'unrelated.txt'), 'Synthetic non-log fixture')
            function Invoke-LogExportSmoke {
                $export = Start-Process $installed -ArgumentList @('--export-log', "`"$zipPath`"") -WindowStyle Hidden -PassThru
                try {
                    if (-not $export.WaitForExit(30000)) {
                        $export.Kill()
                        if (-not $export.WaitForExit(5000)) { throw 'Installed log export did not stop.' }
                        throw 'Installed log export timed out.'
                    }
                    return $export.ExitCode
                } finally { $export.Dispose() }
            }

            if ((Invoke-LogExportSmoke) -ne 0) { throw 'Installed log export failed.' }
            $logsZip = [IO.Compression.ZipFile]::OpenRead($zipPath)
            try {
                if ($logsZip.Entries.Count -ne $expectedLogs.Count) { throw 'Unexpected number of log ZIP entries.' }
                foreach ($name in $expectedLogs.Keys) {
                    $entry = $logsZip.GetEntry($name)
                    if ($null -eq $entry) { throw "Missing log ZIP entry: $name" }
                    $stream = $entry.Open()
                    $bytes = [IO.MemoryStream]::new()
                    try {
                        $stream.CopyTo($bytes)
                        if ([Convert]::ToBase64String($bytes.ToArray()) -cne [Convert]::ToBase64String($expectedLogs[$name])) {
                            throw "Log ZIP entry bytes differ: $name"
                        }
                    } finally { $bytes.Dispose(); $stream.Dispose() }
                }
            } finally { $logsZip.Dispose() }

            $zipHash = (Get-FileHash -LiteralPath $zipPath).Hash
            if ((Invoke-LogExportSmoke) -ne 1) { throw 'Installed log export did not reject an existing destination.' }
            if ((Get-FileHash -LiteralPath $zipPath).Hash -cne $zipHash) { throw 'Installed log export modified the existing ZIP.' }
        } finally {
            $env:LUMADESK_DATA_DIR = $previousDataDir
            if (Test-Path -LiteralPath $logSmoke) { Remove-Item -LiteralPath $logSmoke -Recurse -Force }
        }
    } finally {
        if ($installAttempted) {
            $process = Start-Process msiexec.exe -ArgumentList @('/x', $productCode, '/qn', '/norestart', '/l*v', "`"$release\uninstall.log`"") -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -notin @(0, 3010, 1605)) { throw "MSI uninstall failed: $($process.ExitCode)" }
        }
    }
    if (Test-Path (Join-Path $target 'LumaDesk.exe')) { throw 'Uninstall left the application behind.' }
    if (Test-Path $shortcut) { throw 'Uninstall left the shortcut behind.' }
}
Write-Host 'Package checks passed.'
