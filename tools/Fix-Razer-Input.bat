@echo off
REM  LiteSynapse - Razer input recovery launcher.
REM  Double-click this. It asks for administrator rights, then runs Fix-Razer-Input.ps1
REM  (in the same folder) to remove the broken Razer filter drivers so your keyboard and
REM  mouse work again. REBOOT when it finishes.

setlocal
set "PS1=%~dp0Fix-Razer-Input.ps1"

if not exist "%PS1%" (
    echo Fix-Razer-Input.ps1 was not found next to this file.
    echo Keep both files together in the same folder, then run this again.
    pause
    exit /b 1
)

REM  Re-launch elevated if we are not already administrator.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "Start-Process -FilePath powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','\"%PS1%\"'"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
