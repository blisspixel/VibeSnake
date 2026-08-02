@echo off
REM Double-click launcher for Windows Explorer.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0play.ps1" %*
