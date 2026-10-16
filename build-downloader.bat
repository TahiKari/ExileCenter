@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-downloader.ps1" %*
set EXITCODE=%ERRORLEVEL%
echo.
if %EXITCODE% neq 0 (
    echo Build fehlgeschlagen - Exit-Code %EXITCODE%
) else (
    echo ExileCenterDownloader.exe liegt im Projektordner (~1-2 MB, braucht .NET 10).
    echo Fuer ~50 MB ohne .NET: build-downloader.bat -SelfContained
)
echo.
pause
exit /b %EXITCODE%
