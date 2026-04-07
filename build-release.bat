@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0copy-icon.ps1"
if errorlevel 1 exit /b 1
taskkill /F /IM ReQuit.exe >nul 2>&1

rem dotnet publish does not clean the output folder; wipe it to avoid stale files.
if exist "Publish" rmdir /S /Q "Publish"
mkdir "Publish" >nul 2>&1
dotnet publish ReQuit\ReQuit.csproj -c Release -r win-x64 --self-contained true -o "Publish" ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false

rem Keep output folder truly single-exe (everything is embedded and will self-extract at runtime).
if exist "Publish\ReQuit.exe" (
  for %%F in ("Publish\*") do (
    if /I not "%%~nxF"=="ReQuit.exe" del /F /Q "%%F" >nul 2>&1
  )
)
if %errorlevel%==0 start "" "Publish\ReQuit.exe"
