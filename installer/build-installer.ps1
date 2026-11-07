# Build the ScreenStay installer (Setup exe) and portable zip.
#   Usage: powershell -ExecutionPolicy Bypass -File .\installer\build-installer.ps1
[CmdletBinding()]
param(
    [string]$Version = '1.5.3',
    [string]$DistDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $DistDir) { $DistDir = Join-Path $root 'dist' }

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found.' }
$fw = Split-Path -Parent $csc
$icon = Join-Path $root 'assets\icon-monitor.ico'

$appExe = Join-Path $root 'ScreenStay.exe'
$viewerDir = Join-Path $root 'viewer-dist'
if (-not (Test-Path $appExe)) { throw "ScreenStay.exe not found. Run build.ps1 first." }
if (-not (Test-Path (Join-Path $viewerDir 'ScreenStay.Viewer.exe'))) { throw "viewer-dist not found. Run viewer\build-viewer.ps1 first." }

if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Force -Path $DistDir | Out-Null }

$work = Join-Path $env:TEMP ('ScreenStay-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    # 1. Build uninstaller
    $uninstaller = Join-Path $work 'Uninstall.exe'
    & $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$uninstaller" "/win32icon:$icon" `
        "/win32manifest:$(Join-Path $root 'installer\installer.manifest')" `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        (Join-Path $root 'installer\Uninstall.cs')
    if ($LASTEXITCODE -ne 0) { throw "Uninstaller build failed ($LASTEXITCODE)" }
    Copy-Item -LiteralPath $uninstaller -Destination (Join-Path $DistDir 'Uninstall.exe') -Force

    # 2. Stage payload files (avoid copying ScreenStay.exe through %TEMP%: Defender may flag it)
    $stage = Join-Path $work 'stage'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage -Force
    $helpFile = Get-ChildItem -LiteralPath $root -Filter '*.txt' | Select-Object -First 1
    if ($helpFile -ne $null) { Copy-Item -LiteralPath $helpFile.FullName -Destination $stage -Force }
    Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') -Destination $stage -Force
    Copy-Item -LiteralPath $uninstaller -Destination $stage -Force
    $stageViewer = Join-Path $stage 'ScreenStay.Viewer'
    New-Item -ItemType Directory -Force -Path $stageViewer | Out-Null
    Copy-Item -Path (Join-Path $viewerDir '*') -Destination $stageViewer -Recurse -Force

    # 3. Payload zip
    $payload = Join-Path $work 'payload.zip'
    Compress-Archive -Path (Join-Path $stage '*'), $appExe -DestinationPath $payload -Force

    # 4. Build setup exe (WPF UI, crisp per-monitor DPI rendering)
    $setup = Join-Path $DistDir ("ScreenStay-Setup-$Version.exe")
    if (Test-Path $setup) { Remove-Item $setup -Force }
    $wpf = Join-Path $fw 'WPF'
    & $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$setup" "/win32icon:$icon" `
        "/win32manifest:$(Join-Path $root 'installer\installer.manifest')" `
        "/resource:$payload,ScreenStay.Installer.payload.zip" `
        "/resource:$(Join-Path $root 'installer\SetupWindow.xaml'),ScreenStay.Installer.SetupWindow.xaml" `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Core.dll `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        /reference:"$wpf\PresentationFramework.dll" `
        /reference:"$wpf\PresentationCore.dll" `
        /reference:"$wpf\WindowsBase.dll" `
        /reference:"$fw\System.Xaml.dll" `
        (Join-Path $root 'installer\Setup.cs')
    if ($LASTEXITCODE -ne 0) { throw "Setup build failed ($LASTEXITCODE)" }

    # 5. Portable zip (includes Uninstall.exe)
    $portable = Join-Path $DistDir ("ScreenStay-v$Version.zip")
    if (Test-Path $portable) { Remove-Item $portable -Force }
    Compress-Archive -Path (Join-Path $stage '*'), $appExe -DestinationPath $portable -Force

    Write-Host ("Setup:    {0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
    Write-Host ("Portable: {0}  ({1:N1} MB)" -f $portable, ((Get-Item $portable).Length / 1MB))
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
