# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 LumaDesk contributors

$ErrorActionPreference = 'Stop'
$package = $env:LUMADESK_UPDATE_PACKAGE
try {
    $parent = Get-Process -Id $env:LUMADESK_UPDATE_PARENT -ErrorAction SilentlyContinue
    if ($parent -and !$parent.WaitForExit(60000)) { throw 'LumaDesk is still running.' }

    $target = [Diagnostics.FileVersionInfo]::GetVersionInfo($env:LUMADESK_UPDATE_TARGET)
    $from = '{0}.{1}.{2}' -f $target.FileMajorPart, $target.FileMinorPart, $target.FileBuildPart
    if ($target.ProductName -ne 'LumaDesk' -or $from -ne $env:LUMADESK_UPDATE_FROM) {
        throw 'The installed application changed after the update was downloaded.'
    }
    $isInstaller = [IO.Path]::GetExtension($package) -eq '.msi'
    $file = [IO.File]::OpenRead($package)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($file)).Replace('-', '') }
        finally { $sha.Dispose() }
        if ($digest -ne $env:LUMADESK_UPDATE_HASH) {
            throw 'Update checksum mismatch.'
        }
        if ($isInstaller) {
            $installer = Join-Path ([Environment]::SystemDirectory) 'msiexec.exe'
            $arguments = '/i "{0}" /passive /norestart' -f $package
            $result = Start-Process -FilePath $installer -ArgumentList $arguments -Verb RunAs -Wait -PassThru
            if ($result.ExitCode -notin 0, 3010) { throw ('MSI exit code ' + $result.ExitCode) }
        }
    } finally { $file.Dispose() }
    if (!$isInstaller) {
        [IO.File]::Replace($package, $env:LUMADESK_UPDATE_TARGET, [NullString]::Value)
    }
} catch {
    [IO.File]::AppendAllText($env:LUMADESK_UPDATE_LOG, ('{0:o} Update failed: {1}' -f [DateTime]::Now, $_) + [Environment]::NewLine)
    exit 1
} finally {
    if ([IO.File]::Exists($package)) { [IO.File]::Delete($package) }
}
