# Build ScreenStay.exe
# Uses the .NET Framework 4.x compiler (csc.exe) shipped with Windows.
# No Visual Studio / .NET SDK required.
#   Usage:  powershell -ExecutionPolicy Bypass -File .\build.ps1
[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }

$iconOn = Join-Path $root 'assets\icon-on.ico'
$iconIdle = Join-Path $root 'assets\icon-monitor.ico'
$iconOff = Join-Path $root 'assets\icon-off.ico'
if (-not (Test-Path $iconOn) -or -not (Test-Path $iconIdle) -or -not (Test-Path $iconOff)) {
    Write-Host 'Generating icons ...'
    & (Join-Path $root 'tools\make-icons.ps1') -OutDir (Join-Path $root 'assets')
}

$exe = Join-Path $root 'ScreenStay.exe'
if ($Clean -and (Test-Path $exe)) { Remove-Item $exe -Force }

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    "/out:$exe"
    "/win32icon:$iconIdle"
    "/win32manifest:$(Join-Path $root 'app.manifest')"
    "/resource:$iconOn,KeepAwake.Resources.icon-on.ico"
    "/resource:$iconIdle,KeepAwake.Resources.icon-monitor.ico"
    "/resource:$iconOff,KeepAwake.Resources.icon-off.ico"
    (Join-Path $root 'shared\SunCalculator.cs')
    (Join-Path $root 'shared\NightLightState.cs')
    (Join-Path $root 'src\KeepAwake.cs')
)

Write-Host 'Building ScreenStay.exe ...'
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit code $LASTEXITCODE)" }

$info = Get-Item $exe
Write-Host ("Build OK: {0}  ({1:N1} KB)" -f $info.FullName, ($info.Length / 1KB))
