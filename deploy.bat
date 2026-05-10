@echo off
setlocal
title WolfEQ Deploy
echo ==========================================
echo   WolfEQ - Build and Launch
echo ==========================================
echo.

cd /d "%~dp0"

set "APP_NAME=WolfEQ"
set "PROJECT_FILE=WolfEQ.csproj"
set "APP_EXE=%~dp0bin\Debug\net8.0-windows10.0.19041.0\WolfEQ.exe"

echo [1/5] Checking optional Git sync...
if exist ".git" (
    git -c safe.directory=* remote get-url origin >nul 2>nul
    if errorlevel 1 (
        echo      No origin remote configured. Skipping pull.
    ) else (
        git -c safe.directory=* pull --ff-only
        if errorlevel 1 (
            echo ERROR: Git pull failed. Check your connection or local changes.
            pause
            exit /b 1
        )
    )
) else (
    echo      No .git folder here. Skipping pull.
)

echo.
echo [2/5] Killing old WolfEQ if running...
taskkill /f /im "%APP_NAME%.exe" 2>nul
timeout /t 2 /nobreak >nul

echo.
echo [3/5] Cleaning stale WPF temp build files...
for %%D in ("obj\Debug\net8.0-windows" "obj\Release\net8.0-windows") do (
    if exist %%~D (
        del /q "%%~D\WolfEQ_*_wpftmp.*" 2>nul
    )
)

echo.
echo [4/5] Building...
dotnet build "%PROJECT_FILE%" -c Debug --no-incremental
if errorlevel 1 (
    echo ERROR: Build failed. Check errors above.
    pause
    exit /b 1
)

echo.
echo [5/5] Launching WolfEQ...
if not exist "%APP_EXE%" (
    echo ERROR: Expected app was not found:
    echo        %APP_EXE%
    pause
    exit /b 1
)

start "" "%APP_EXE%"

echo.
echo WolfEQ deployed and launched.
timeout /t 2 /nobreak >nul
