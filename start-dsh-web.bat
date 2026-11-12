@echo off
setlocal
chcp 65001 >nul
title DSH Web - START

rem Start the DSH web host to control this PC from a phone (over Tailscale).
rem Double-click this file. Closing this window does NOT stop the server.
rem The login cookie is capped at 1 day via --patch connection-1day.patch.yml.
rem   start-dsh-web.bat                         : show the QR on the PC
rem   start-dsh-web.bat -Taildrop your-phone    : send the pairing link over Taildrop
rem   (if Taildrop fails, it falls back to showing the QR on the PC)

set "PS=pwsh"
where pwsh >nul 2>nul || set "PS=powershell"

if not exist "%~dp0dsh-web-start.ps1" (
    echo [ERROR] dsh-web-start.ps1 not found in the same folder.
    pause
    exit /b 1
)

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0dsh-web-start.ps1" %*
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" echo [OK] Host is running. Stop it with stop-dsh-web.bat
pause
exit /b %RC%
