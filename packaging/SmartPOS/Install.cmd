@echo off
setlocal
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Install.ps1"
exit /b %ERRORLEVEL%
