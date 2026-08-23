@echo off
setlocal
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-LocalUser.ps1" -ReportPath "%USERPROFILE%\Desktop\STG-Test-Report.txt"
pause