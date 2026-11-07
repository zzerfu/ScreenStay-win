# Build the WinUI 3 viewer (KeepAwake.Viewer).
# Requires the .NET 8 SDK (dotnet) and NuGet access for the Windows App SDK packages.
# The Windows App Runtime must be installed on the target machine (framework-dependent build).
#   Usage:  powershell -ExecutionPolicy Bypass -File .\build-viewer.ps1
#           powershell -ExecutionPolicy Bypass -File .\build-viewer.ps1 -Dotnet C:\path\to\dotnet.exe
[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$viewerDir = $PSScriptRoot
if (-not $viewerDir) { $viewerDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $viewerDir
$project = Join-Path $viewerDir 'KeepAwake.Viewer.csproj'

if (-not $OutDir) { $OutDir = Join-Path $root 'viewer-dist' }

Write-Host "Building WinUI 3 viewer with: $Dotnet"
& $Dotnet build $project -c Release -r win-x64
if ($LASTEXITCODE -ne 0) { throw "Viewer build failed (dotnet exit code $LASTEXITCODE)" }

# Use the dotnet build output (contains .pri / .xbf).
# Do NOT use dotnet publish: its output is missing the WinUI PRI/XBF files and the app
# crashes at startup (0xc000027b in Microsoft.UI.Xaml.dll).
$bin = Join-Path $viewerDir 'bin\Release\net8.0-windows10.0.22621.0\win-x64'
if (-not (Test-Path -LiteralPath $bin)) { throw "Viewer build output not found: $bin" }
$exePath = Join-Path $bin 'ScreenStay.Viewer.exe'
$priPath = Join-Path $bin 'ScreenStay.Viewer.pri'
if (-not (Test-Path -LiteralPath $exePath)) { throw "ScreenStay.Viewer.exe not found: $exePath" }
if (-not (Test-Path -LiteralPath $priPath)) { throw "ScreenStay.Viewer.pri not found: $priPath" }

if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Copy-Item -Path (Join-Path $bin '*') -Destination $OutDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'assets\icon-monitor.ico') -Destination (Join-Path $OutDir 'app.ico') -Force

Get-ChildItem -LiteralPath $OutDir -Filter '*.pdb' -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force
}

$size = (Get-ChildItem -LiteralPath $OutDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("Viewer published: {0}  ({1:N1} MB)" -f $OutDir, ($size / 1MB))
