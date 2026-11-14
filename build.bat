@echo off
REM ===========================================================
REM  AmdPanel v1.0.0 - One-click Build (CMD wrapper for build.ps1)
REM  Requires PowerShell 5.1+ (built-in on Windows 10/11)
REM ===========================================================
setlocal enabledelayedexpansion

set CONFIG=Release
set PLATFORM=x64
if not "%1"=="" set CONFIG=%1
if not "%2"=="" set PLATFORM=%2

where powershell >nul 2>nul
if errorlevel 1 (
    echo ERROR: PowerShell not found on PATH. Windows 10/11 ships with PowerShell 5.1 by default.
    exit /b 1
)

REM Try to bypass ExecutionPolicy for the local script
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration %CONFIG% -Platform %PLATFORM%
set EXITCODE=%ERRORLEVEL%

if %EXITCODE%==0 (
    echo.
    echo Done. See dist\ folder for the release archive.
) else (
    echo.
    echo Build failed with exit code %EXITCODE%.
)

endlocal & exit /b %EXITCODE%
