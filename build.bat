@echo off

REM Parse optional module exclusion flags
set MODULE_PROPS=
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--no-qq"    set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleQQ=false
if /i "%~1"=="--no-wecom" set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleWeCom=false
if /i "%~1"=="--no-unity" set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleUnity=false
if /i "%~1"=="--no-github-tracker" set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleGitHubTracker=false
shift
goto :parse_args
:args_done

if exist "build" (
    rmdir /s /q "build"
)
mkdir "build"

cd src/DotCraft.App

echo.
echo =====================================
echo Extracting version number...
echo =====================================
echo.

REM Set default version in case extraction fails
set VERSION=0.0.0

REM Extract version using PowerShell for better reliability
for /f "delims=" %%i in ('powershell -Command "(Select-Xml -Path 'DotCraft.App.csproj' -XPath '//Version').Node.InnerText"') do set VERSION=%%i

REM If PowerShell method failed, try manual parsing
if "%VERSION%"=="0.0.0" (
    echo Trying alternative version extraction method...
    for /f "tokens=2 delims=>" %%a in ('findstr /C:"<Version>" DotCraft.App.csproj 2^>nul') do (
        for /f "tokens=1 delims=<" %%b in ("%%a") do set VERSION=%%b
    )
)

REM Remove any whitespace
for /f "tokens=* delims= " %%a in ("%VERSION%") do set VERSION=%%a
echo Version found: %VERSION%

echo.
echo =====================================
echo  Building DotCraft...
echo =====================================
echo.

call dotnet publish /p:PublishProfile=ReleaseProfile%MODULE_PROPS%

if %ERRORLEVEL% neq 0 (
    echo Build DotCraft failed with exit code %ERRORLEVEL%.
    goto :failure
)

goto :success

:failure
echo.
echo Installation failed. Please try again.
echo.
pause
exit /b 1

:success
echo.
echo =====================================
echo  Build completed successfully!
echo =====================================
echo.

cd ../..

echo.
echo =====================================
echo  Packaging...
echo =====================================
echo.

REM Copy helper scripts to CLI output directory
copy /Y "Scripts\install_to_path.ps1" "build\release\install_to_path.ps1"

REM Create zip for dotcraft
echo Creating dotcraft.zip...
powershell -Command "Compress-Archive -Path 'build\release\*' -DestinationPath 'build\release\dotcraft_v%VERSION%.zip' -Force"

echo.
echo =====================================
echo  Packaging completed!
echo =====================================
echo  - dotcraft.zip created
echo =====================================
echo.
pause 