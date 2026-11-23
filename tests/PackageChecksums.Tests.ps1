$ErrorActionPreference = 'Stop'
$check = Join-Path (Split-Path $PSScriptRoot) 'tools/Test-ReleaseChecksums.ps1'
$directory = Join-Path ([IO.Path]::GetTempPath()) "LumaDesk-checksums-$([Guid]::NewGuid())"
$version = '0.5.0'
$names = @("LumaDesk-$version-win-x64.exe", "LumaDesk-$version-slim-win-x64.exe", "LumaDesk-$version-portable-win-x64.zip", "LumaDesk-$version-win-x64.msi")

function Assert-Rejected([string]$Scenario, [string[]]$Lines, [string]$ExpectedError) {
    [IO.File]::WriteAllLines((Join-Path $directory 'SHA256SUMS.txt'), $Lines)
    try { & $check -ReleaseDirectory $directory -Version $version }
    catch {
        if ($_.Exception.Message -notlike $ExpectedError) { throw "${Scenario}: unexpected error: $_" }
        Write-Host "PASS: $Scenario"
        return
    }
    throw "${Scenario}: invalid release checksums were accepted."
}

New-Item -ItemType Directory -Path $directory | Out-Null
try {
    # Tiny inert files exercise the real verifier without building, installing, or launching packages.
    $valid = @(foreach ($name in $names) {
        $path = Join-Path $directory $name
        [IO.File]::WriteAllText($path, "fixture for $name")
        "{0}  {1}" -f (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(), $name
    })
    [IO.File]::WriteAllLines((Join-Path $directory 'SHA256SUMS.txt'), $valid)
    & $check -ReleaseDirectory $directory -Version $version
    Write-Host 'PASS: complete release manifest'

    # Ordering and hash letter case do not affect validity.
    $reordered = $valid.Clone()
    [array]::Reverse($reordered)
    $reordered = @($reordered | ForEach-Object { $_.Substring(0, 64).ToUpperInvariant() + $_.Substring(64) })
    [IO.File]::WriteAllLines((Join-Path $directory 'SHA256SUMS.txt'), $reordered)
    & $check -ReleaseDirectory $directory -Version $version
    Write-Host 'PASS: reordered entries and uppercase hashes'

    Assert-Rejected 'empty manifest' @() 'Checksum manifest must contain exactly*'
    for ($i = 0; $i -lt $names.Count; $i++) {
        $incomplete = @($valid | Where-Object { $_ -ne $valid[$i] })
        Assert-Rejected "missing $($names[$i])" $incomplete 'Checksum manifest must contain exactly*'
    }
    $tail = @($valid | Select-Object -Skip 1)
    Assert-Rejected 'duplicate replaces required file' (@($valid[1]) + $tail) 'Unexpected or duplicate checksum entry:*'
    Assert-Rejected 'extra entry' ($valid + $valid[0]) 'Checksum manifest must contain exactly*'
    Assert-Rejected 'blank entry' (@('') + $tail) 'Invalid checksum manifest entry.'
    Assert-Rejected 'short hash' (@($valid[0].Substring(1)) + $tail) 'Invalid checksum manifest entry.'
    Assert-Rejected 'non-hex hash' (@('z' + $valid[0].Substring(1)) + $tail) 'Invalid checksum manifest entry.'
    Assert-Rejected 'malformed separator' (@($valid[0].Replace('  ', ' ')) + $tail) 'Invalid checksum manifest entry.'
    foreach ($invalidName in @('unexpected.exe', 'LumaDesk-0.4.0-win-x64.exe', "../$($names[0])", "..\$($names[0])", (Join-Path $directory $names[0]))) {
        Assert-Rejected "unexpected name $invalidName" (@($valid[0].Substring(0, 66) + $invalidName) + $tail) 'Unexpected or duplicate checksum entry:*'
    }

    # Corruption of each artifact must fail, including the standalone EXE.
    for ($i = 0; $i -lt $names.Count; $i++) {
        $path = Join-Path $directory $names[$i]
        [IO.File]::WriteAllText($path, 'corrupted')
        Assert-Rejected "tampered $($names[$i])" $valid 'Checksum mismatch:*'
        [IO.File]::WriteAllText($path, "fixture for $($names[$i])")
    }
    Remove-Item -LiteralPath (Join-Path $directory $names[0])
    Assert-Rejected 'missing artifact' $valid '*'
    Remove-Item -LiteralPath (Join-Path $directory 'SHA256SUMS.txt')
    try {
        & $check -ReleaseDirectory $directory -Version $version
        throw 'Missing manifest was accepted.'
    } catch [System.Management.Automation.ItemNotFoundException] {
        Write-Host 'PASS: missing manifest'
    }
} finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
Write-Host 'Package checksum tests passed.'
