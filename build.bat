@echo off
REM Convenience launcher for build.ps1.
REM Bypasses the default PowerShell execution policy so the script runs on a fresh machine.

setlocal
set SCRIPT_DIR=%~dp0
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build.ps1" %*
exit /b %ERRORLEVEL%
