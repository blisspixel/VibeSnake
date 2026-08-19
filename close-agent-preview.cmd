@echo off
REM One-command close-out for the current Agent Arena preview slice.
REM Run from cmd.exe or Explorer. Sets the repo SDK before any .NET tool starts.
setlocal EnableExtensions
cd /d "%~dp0"

set "DOTNET_ROOT=%~dp0.dotnet"
set "DOTNET_ROOT_X64=%DOTNET_ROOT%"
set "PATH=%DOTNET_ROOT%;%PATH%"

set "PY="
where py >nul 2>nul
if not errorlevel 1 set "PY=py -3"
if not defined PY (
  where python >nul 2>nul
  if not errorlevel 1 set "PY=python"
)
if not defined PY (
  echo Python 3 is required. Install it or add py.exe to PATH.
  exit /b 1
)

if not exist "%DOTNET_ROOT%\dotnet.exe" (
  echo Missing repo SDK at "%DOTNET_ROOT%\dotnet.exe"
  exit /b 1
)

echo Using DOTNET_ROOT=%DOTNET_ROOT%
%PY% "%~dp0scripts\close_agent_preview.py" %*
exit /b %ERRORLEVEL%
