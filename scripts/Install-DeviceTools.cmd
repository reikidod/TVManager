@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-DeviceTools.ps1" %*
if errorlevel 1 (
  echo.
  echo Tool setup failed. See the error above.
  pause
  exit /b 1
)
echo.
pause
