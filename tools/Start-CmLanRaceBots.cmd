@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-CmLanRaceBots.ps1"
set "racebots_exit=%ERRORLEVEL%"
if not "%racebots_exit%"=="0" pause
exit /b %racebots_exit%
