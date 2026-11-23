param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
$prefix = "LumaDesk-$Version"
$remaining = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($name in @("$prefix-win-x64.exe", "$prefix-slim-win-x64.exe", "$prefix-portable-win-x64.zip", "$prefix-win-x64.msi")) {
    [void]$remaining.Add($name)
}
$lines = @(Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'SHA256SUMS.txt'))
if ($lines.Count -ne $remaining.Count) { throw "Checksum manifest must contain exactly $($remaining.Count) release files." }
foreach ($line in $lines) {
    if ($line -cnotmatch '^([0-9a-fA-F]{64})  (.+)$') { throw 'Invalid checksum manifest entry.' }
    $hash, $name = $Matches[1], $Matches[2]
    # Only expected basenames are allowed; reject duplicates and paths before reading a file.
    if (-not $remaining.Remove($name)) { throw "Unexpected or duplicate checksum entry: $name" }
    if ((Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory $name) -Algorithm SHA256).Hash -ne $hash) {
        throw "Checksum mismatch: $name"
    }
}
