@echo off
setlocal EnableExtensions

cd /d "%~dp0"

echo ========================================
echo   ThatUtilsPad build
echo ========================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet SDK is not installed or not on PATH.
    exit /b 1
)

set "CONFIG=Release"
if /I "%~1"=="Debug" set "CONFIG=Debug"
if /I "%~1"=="debug" set "CONFIG=Debug"

if exist "ThatUtilsPad\ThatUtilsPad.csproj" (
    set "PROJECT=ThatUtilsPad\ThatUtilsPad.csproj"
) else if exist "ThatUtilsPad.sln" (
    set "PROJECT=ThatUtilsPad.sln"
) else (
    echo [ERROR] No ThatUtilsPad.csproj or ThatUtilsPad.sln found in:
    echo   %CD%
    exit /b 1
)

echo Project: %PROJECT%
echo Config:  %CONFIG%
echo.

echo [1/2] Restoring...
dotnet restore "%PROJECT%"
if errorlevel 1 (
    echo [ERROR] Restore failed.
    exit /b 1
)

echo [2/2] Building...
dotnet build "%PROJECT%" -c %CONFIG% --no-restore
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

echo.
echo ----------------------------------------
echo Build succeeded ^(%CONFIG%^)
if exist "ThatUtilsPad\bin\%CONFIG%\netstandard2.1\ThatUtilsPad.dll" (
    echo Output: ThatUtilsPad\bin\%CONFIG%\netstandard2.1\ThatUtilsPad.dll
)
echo ----------------------------------------
echo.
echo Tip: in PowerShell use:  .\build.bat
echo      Debug build:         .\build.bat Debug
echo.
pause
endlocal
exit /b 0
