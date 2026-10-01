@echo off
REM One-command close-out for the current Agent Arena preview slice.
REM Run from cmd.exe or Explorer. PowerShell selects the repo SDK before .NET tools.
setlocal EnableExtensions
cd /d "%~dp0"

where pwsh >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 is required. Install it or add pwsh.exe to PATH.
  exit /b 1
)
pwsh -NoProfile -File "%~dp0scripts\close_agent_preview.ps1" %*
exit /b %ERRORLEVEL%
