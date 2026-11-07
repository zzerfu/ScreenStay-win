@echo off
rem Build the WinUI 3 viewer. Requires the .NET 8 SDK (dotnet) in PATH.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-viewer.ps1" %*
if errorlevel 1 (
    echo.
    echo Viewer build failed.
    pause
)
endlocal
