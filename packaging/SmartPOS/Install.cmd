@echo off
setlocal
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
exit /b %ERRORLEVEL%
