@echo off
setlocal
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
set "SOURCE=%~dp0"
if "%SOURCE:~-1%"=="\" set "SOURCE=%SOURCE:~0,-1%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SOURCE%\Setup.ps1" -SourceFolder "%SOURCE%"
pause