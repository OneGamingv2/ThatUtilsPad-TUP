@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0"

echo ========================================
echo   ThatUtilsPad sync
echo ========================================
echo.

where git >nul 2>&1
if errorlevel 1 (
    echo [ERROR] git is not installed or not on PATH.
    exit /b 1
)

if not exist ".git" (
    echo [ERROR] No git repo found in:
    echo   %CD%
    exit /b 1
)

for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "BRANCH=%%b"
if not defined BRANCH set "BRANCH=main"

echo Repo:    %CD%
echo Branch:  %BRANCH%
echo Remote:  origin
echo.

echo [1/5] Fetching from GitHub...
git fetch --prune origin
if errorlevel 1 (
    echo [ERROR] git fetch failed.
    exit /b 1
)

set "STASHED=0"

git status --porcelain > "%TEMP%\tup_sync_status.txt" 2>nul
for /f %%A in ('type "%TEMP%\tup_sync_status.txt" ^| find /c /v ""') do set "STATUS_LINES=%%A"

if "%STATUS_LINES%"=="0" (
    echo [2/5] Working tree clean - no stash needed.
    goto :pull
)

echo [2/5] Local / untracked changes detected - stashing...
git stash push -u -m "sync.bat auto-stash %DATE% %TIME%"
if errorlevel 1 (
    echo [WARN] stash failed - backing up sync.bat if present...
    if exist "sync.bat" (
        if exist "sync.bat.localbak" del /f /q "sync.bat.localbak"
        ren "sync.bat" "sync.bat.localbak"
        echo Backed up to sync.bat.localbak
    )
) else (
    set "STASHED=1"
)

:pull
echo [3/5] Pulling latest %BRANCH%...
git pull --ff-only origin "%BRANCH%"
if errorlevel 1 (
    echo Fast-forward failed - trying merge pull...
    git pull origin "%BRANCH%"
    if errorlevel 1 (
        echo.
        echo [ERROR] git pull failed.
        echo If it says sync.bat would be overwritten, run:
        echo   del sync.bat
        echo   git pull
        echo   .\sync.bat
        if "%STASHED%"=="1" echo Your stashed changes are in: git stash list
        exit /b 1
    )
)

if "%STASHED%"=="1" (
    echo [4/5] Restoring your local stashed changes...
    git stash pop
    if errorlevel 1 (
        echo [WARN] stash pop had conflicts. Fix them, then: git stash drop
        echo Keeping GitHub sync.bat if both exist.
    )
) else (
    echo [4/5] No stash to restore.
    if exist "sync.bat.localbak" (
        echo Note: old local sync.bat was saved as sync.bat.localbak
        echo GitHub sync.bat is now active. Delete the .localbak when done.
    )
)

echo [5/5] Restoring / updating project dependencies...
if exist "ThatUtilsPad\ThatUtilsPad.csproj" (
    where dotnet >nul 2>&1
    if errorlevel 1 (
        echo [WARN] dotnet SDK not found - skipped restore/build.
    ) else (
        echo Running dotnet restore...
        dotnet restore "ThatUtilsPad\ThatUtilsPad.csproj"
        echo Building Release...
        dotnet build "ThatUtilsPad\ThatUtilsPad.csproj" -c Release --no-restore
        if errorlevel 1 (
            echo [WARN] Build reported errors. Sync still completed.
        ) else (
            echo Build succeeded.
        )
    )
) else if exist "ThatUtilsPad.sln" (
    where dotnet >nul 2>&1
    if not errorlevel 1 (
        dotnet restore "ThatUtilsPad.sln"
        dotnet build "ThatUtilsPad.sln" -c Release --no-restore
    )
) else (
    echo [WARN] No .csproj / .sln found to restore.
)

echo.
echo ----------------------------------------
git status -sb
echo ----------------------------------------
echo Sync complete.
echo.
echo Tip: in PowerShell use:  .\sync.bat
echo.
pause
endlocal
exit /b 0
