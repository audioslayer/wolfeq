@echo off
setlocal
cd /d "%~dp0"

echo ============================================
echo   WolfEQ - Build Installer
echo ============================================
echo.

rem Equalizer APO installer bundled for Windows System EQ (GPL-2.0, shipped unmodified).
rem The exe is gitignored. It is downloaded when missing and must match the pinned SHA-256.
rem Pinned hash: Chocolatey equalizerapo 1.4.2 checksum64, confirmed against the SourceForge download.
set "APO_VERSION=1.4.2"
set "APO_FILE=EqualizerAPO-x64-%APO_VERSION%.exe"
set "APO_DIR=%~dp0redist\EqualizerAPO"
set "APO_EXE=%APO_DIR%\%APO_FILE%"
set "APO_URL=https://sourceforge.net/projects/equalizerapo/files/%APO_VERSION%/%APO_FILE%/download"
set "APO_SHA256=7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa"

echo [1/3] Checking bundled Equalizer APO %APO_VERSION% installer...
if not exist "%APO_DIR%" mkdir "%APO_DIR%"
if exist "%APO_EXE%" goto :apo_verify

echo      Downloading %APO_FILE% from SourceForge...
if exist "%APO_EXE%.download" del /f /q "%APO_EXE%.download"
where curl.exe >nul 2>nul
if errorlevel 1 goto :apo_download_ps
curl.exe -fsSL --retry 3 -o "%APO_EXE%.download" "%APO_URL%"
goto :apo_download_done

:apo_download_ps
rem SourceForge serves an HTML page to browser user agents, so present a curl-style agent.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; try { Invoke-WebRequest -UseBasicParsing -UserAgent 'curl/8.0' -Uri $env:APO_URL -OutFile ($env:APO_EXE + '.download'); exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"

:apo_download_done
if errorlevel 1 (
    if exist "%APO_EXE%.download" del /f /q "%APO_EXE%.download"
    echo ERROR: Could not download Equalizer APO from:
    echo        %APO_URL%
    echo        Download it manually to %APO_EXE%
    echo        and re-run this script.
    pause
    exit /b 1
)
move /y "%APO_EXE%.download" "%APO_EXE%" >nul

:apo_verify
powershell -NoProfile -ExecutionPolicy Bypass -Command "$h = (Get-FileHash -Algorithm SHA256 -LiteralPath $env:APO_EXE).Hash; if ($h -ieq $env:APO_SHA256) { exit 0 }; Write-Host ('       Expected: ' + $env:APO_SHA256.ToLowerInvariant()); Write-Host ('       Actual:   ' + $h.ToLowerInvariant()); exit 1"
if errorlevel 1 (
    echo ERROR: SHA-256 mismatch for %APO_FILE%.
    echo        The file does not match the pinned Equalizer APO %APO_VERSION% release
    echo        and has been deleted so it cannot be bundled. Re-run to download it again.
    echo        If the mismatch persists, the upstream file changed: do not ship until verified.
    del /f /q "%APO_EXE%"
    pause
    exit /b 1
)
echo      Verified %APO_FILE% (SHA-256 OK)
echo.

if exist publish rmdir /s /q publish
if exist installer\output rmdir /s /q installer\output

echo [2/3] Publishing WolfEQ self-contained...
dotnet publish WolfEQ.csproj -c Release -r win-x64 --self-contained -o publish -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)
if not exist "publish\redist\EqualizerAPO\%APO_FILE%" (
    echo ERROR: publish\redist\EqualizerAPO\%APO_FILE% is missing from the publish output.
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

echo [3/3] Building installer...
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
