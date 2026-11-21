@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-To-GitHub.ps1" %*
if errorlevel 1 (
  echo.
  echo GitHub publishing failed. See the error above.
  pause
  exit /b 1
)
echo.
pause
