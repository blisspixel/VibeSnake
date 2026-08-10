@echo off
REM Double-click launcher for Windows Explorer.
cd /d "%~dp0"
where pwsh >nul 2>nul
if errorlevel 1 (
  echo Vibe Snake requires PowerShell 7 or newer.
  exit /b 1
)
pwsh -NoProfile -File "%~dp0play.ps1" %*
exit /b %errorlevel%
