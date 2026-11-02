param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $sdk)) { $sdk = 'dotnet' }
$project = Join-Path $PSScriptRoot 'src\LumaDesk\LumaDesk.csproj'
$destination = Join-Path $PSScriptRoot 'artifacts\LumaDesk-win-x64'
if ($Portable) {
    & $sdk publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $destination
} else {
    & $sdk publish $project -c Release --self-contained false -o $destination
}
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $destination -Force
$docDestination = Join-Path $destination 'docs'
New-Item -ItemType Directory -Path $docDestination -Force | Out-Null
foreach ($name in @('validation.md', 'color-hdr.md', 'display-layout.md', 'virtual-displays.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "docs\$name") -Destination $docDestination -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\third-party') -Destination $docDestination -Recurse -Force
if (Test-Path (Join-Path $PSScriptRoot 'docs\images')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\images') -Destination $docDestination -Recurse -Force }
Write-Host "Ready: $destination\LumaDesk.exe"
