@echo off
setlocal
set SCRIPT_DIR=%~dp0
echo Packaging AirFlick Portable Release...
powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%package_portable.ps1"
echo Done.
pause

