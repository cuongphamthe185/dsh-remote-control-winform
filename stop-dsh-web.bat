@echo off
setlocal
chcp 65001 >nul
title DSH Web - STOP

rem Stop the DSH web host + delete all QR images + rotate the token (default).
rem   stop-dsh-web.bat           : stop + delete QR + rotate token
rem   stop-dsh-web.bat keep      : stop but KEEP the existing login
rem   stop-dsh-web.bat task      : schedule via Task Scheduler (works from an agent session)
rem   stop-dsh-web.bat dry       : only show what would happen

set "PS=pwsh"
where pwsh >nul 2>nul || set "PS=powershell"

if not exist "%~dp0dsh-web-stop.ps1" (
    echo [ERROR] dsh-web-stop.ps1 not found in the same folder.
    pause
    exit /b 1
)

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0dsh-web-stop.ps1" %*
set "RC=%ERRORLEVEL%"

echo.
pause
exit /b %RC%
