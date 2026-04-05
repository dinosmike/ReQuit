@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0copy-icon.ps1"
if errorlevel 1 exit /b 1
taskkill /F /IM ReQuit.exe >nul 2>&1
dotnet publish ReQuit\ReQuit.csproj -c Release -r win-x64 --self-contained false -o "Publish" ^
  -p:PublishSingleFile=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false
if %errorlevel%==0 start "" "Publish\ReQuit.exe"
