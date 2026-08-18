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

echo [1/3] Building native RenderBridge (D3D11 Present hook)...
set "MSBUILD="
if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
)
if exist "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
)
if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)

if defined MSBUILD (
    "%MSBUILD%" "ThatUtilsPad.RenderBridge\ThatUtilsPad.RenderBridge.vcxproj" /p:Configuration=%CONFIG% /p:Platform=x64 /v:m
    if errorlevel 1 (
        echo [WARN] Native RenderBridge build failed — using existing Assets\Native copy if present.
    ) else (
        echo Native OK: ThatUtilsPad.RenderBridge\bin\%CONFIG%\ThatUtilsPad.RenderBridge.dll
        if not exist "ThatUtilsPad\Assets\Native\" mkdir "ThatUtilsPad\Assets\Native"
        copy /Y "ThatUtilsPad.RenderBridge\bin\%CONFIG%\ThatUtilsPad.RenderBridge.dll" "ThatUtilsPad\Assets\Native\ThatUtilsPad.RenderBridge.dll" >nul
        echo Embedded staging: ThatUtilsPad\Assets\Native\ThatUtilsPad.RenderBridge.dll
    )
) else (
    echo [WARN] MSBuild not found — skip native rebuild. Using Assets\Native if present.
)

if not exist "ThatUtilsPad\Assets\Native\ThatUtilsPad.RenderBridge.dll" (
    echo [WARN] No native DLL staged for embed — ThatUtilsPad.dll will ship without RenderBridge.
)

echo [2/3] Restoring / building C# (embeds native into ThatUtilsPad.dll)...
dotnet restore "%PROJECT%"
if errorlevel 1 (
    echo [ERROR] Restore failed.
    exit /b 1
)
dotnet build "%PROJECT%" -c %CONFIG% --no-restore
if errorlevel 1 (
    echo [ERROR] C# build failed.
    exit /b 1
)

echo [3/3] Optional deploy to Gorilla Tag plugins (single DLL)...
set "GTPLUGINS=%ProgramFiles(x86)%\Steam\steamapps\common\Gorilla Tag\BepInEx\plugins\ThatUtilsPad"
if exist "%GTPLUGINS%\" (
    if exist "ThatUtilsPad\bin\%CONFIG%\netstandard2.1\ThatUtilsPad.dll" (
        copy /Y "ThatUtilsPad\bin\%CONFIG%\netstandard2.1\ThatUtilsPad.dll" "%GTPLUGINS%\" >nul
        echo Copied ThatUtilsPad.dll
        echo Note: RenderBridge extracts beside it on first load — no separate DLL needed.
    )
) else (
    echo GT plugins folder not found — skip auto-deploy.
)

echo.
echo ----------------------------------------
echo Build finished ^(%CONFIG%^)
echo Ship:   ThatUtilsPad\bin\%CONFIG%\netstandard2.1\ThatUtilsPad.dll
echo         (contains embedded ThatUtilsPad.RenderBridge.dll)
echo Native: ThatUtilsPad.RenderBridge\bin\%CONFIG%\ThatUtilsPad.RenderBridge.dll
echo ----------------------------------------
echo.
pause
endlocal
exit /b 0
