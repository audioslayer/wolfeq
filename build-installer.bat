@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo   WolfEQ - Build Installer
echo ============================================
echo.

if exist publish rmdir /s /q publish
if exist installer\output rmdir /s /q installer\output

echo [1/2] Publishing WolfEQ self-contained...
dotnet publish WolfEQ.csproj -c Release -r win-x64 --self-contained -o publish -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)
echo      Published to .\publish\
echo.

set APP_VERSION=
for /f "tokens=3 delims=<>" %%v in ('findstr /r /c:"^[ ]*<Version>.*</Version>" "%~dp0WolfEQ.csproj"') do set APP_VERSION=%%v
if "%APP_VERSION%"=="" (
    echo ERROR: Could not extract version from WolfEQ.csproj.
    echo        Make sure WolfEQ.csproj has a ^<Version^> tag.
    pause
    exit /b 1
)
if not exist installer mkdir installer
echo #define MyAppVersion "%APP_VERSION%" > "%~dp0installer\version.iss"
echo      Version: %APP_VERSION%
echo.

echo [2/2] Building installer...
where iscc >nul 2>nul
if errorlevel 1 (
    if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\wolfeq-setup.iss
    ) else (
        echo ERROR: Inno Setup not found. Install from https://jrsoftware.org/isinfo.php
        echo        Then re-run this script.
        pause
        exit /b 1
    )
) else (
    iscc installer\wolfeq-setup.iss
)

if errorlevel 1 (
    echo ERROR: Installer build failed.
    pause
    exit /b 1
)

echo.
echo ============================================
echo   Done. Installer at:
echo   installer\output\WolfEQ-Setup-%APP_VERSION%.exe
echo ============================================
pause
