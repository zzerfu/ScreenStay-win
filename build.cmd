@echo off
rem Build KeepAwake.exe using the built-in .NET Framework compiler.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
)
endlocal
