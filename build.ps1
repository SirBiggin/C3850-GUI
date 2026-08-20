<#
.SYNOPSIS
  Builds C3850 GUI as a self-contained single-file Windows app and wraps it in an Inno Setup installer.
.EXAMPLE
  .\build.ps1             # dist\publish\C3850GUI.exe + dist\C3850-GUI-Setup-<ver>.exe
  .\build.ps1 -NoInstaller
#>
param(
    [switch]$NoInstaller,
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "src\C3850GUI\C3850GUI.csproj"
$publish = Join-Path $root "dist\publish"
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

Write-Host "== Publishing C3850 GUI $version ($Configuration)" -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $proj -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($NoInstaller) { Write-Host "Done: $publish\C3850GUI.exe"; exit 0 }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or use -NoInstaller." }

Write-Host "== Building installer" -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $root "installer\C3850GUI.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Get-ChildItem (Join-Path $root "dist\C3850-GUI-Setup-$version.exe") | ForEach-Object { Write-Host "Done: $($_.FullName) ($([math]::Round($_.Length/1MB,1)) MB)" -ForegroundColor Green }

