@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-And-Push.ps1"
if errorlevel 1 (
  echo.
  echo Build or GitHub push failed. Review the error above.
  pause
  exit /b 1
)
echo.
echo Build and GitHub push completed.
pause
