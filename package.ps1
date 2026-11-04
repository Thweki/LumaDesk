param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $sdk)) { $sdk = 'dotnet' }
[xml]$project = Get-Content (Join-Path $PSScriptRoot 'src\LumaDesk\LumaDesk.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part release version.' }
$publish = Join-Path $PSScriptRoot 'artifacts\LumaDesk-win-x64'
$release = Join-Path $PSScriptRoot 'artifacts\release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Portable }
$exe = Join-Path $publish 'LumaDesk.exe'
if (-not (Test-Path $exe)) { throw 'Build the portable app first.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion.Split('+')[0] -ne $version) { throw 'Built EXE version differs from the project version.' }
$prefix = "LumaDesk-$version"
Copy-Item -LiteralPath $exe -Destination (Join-Path $release "$prefix-win-x64.exe") -Force
$zip = Join-Path $release "$prefix-portable-win-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
& $sdk build (Join-Path $PSScriptRoot 'installer\LumaDesk.wixproj') -c Release "-p:AppVersion=$version" "-p:PublishDir=$publish"
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
$msi = Join-Path $release "$prefix-win-x64.msi"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'installer\bin\Release\LumaDesk.msi') -Destination $msi -Force
$files = @((Join-Path $release "$prefix-win-x64.exe"), $zip, $msi)
$sums = foreach ($file in $files) { "{0}  {1}" -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file) }
$sums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Release $version ready: $release"
