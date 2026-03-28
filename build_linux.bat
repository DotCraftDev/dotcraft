@echo off

REM Parse optional module exclusion flags
set MODULE_PROPS=
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--no-qq"    set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleQQ=false
if /i "%~1"=="--no-wecom" set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleWeCom=false
if /i "%~1"=="--no-unity" set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleUnity=false
if /i "%~1"=="--no-github-tracker" set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleGitHubTracker=false
if /i "%~1"=="--no-agui"         set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleAGUI=false
if /i "%~1"=="--no-api"          set MODULE_PROPS=%MODULE_PROPS% /p:IncludeModuleAPI=false
shift
goto :parse_args
:args_done

if exist "build\linux" (
    rmdir /s /q "build\linux"
)
mkdir "build\linux"

cd src/DotCraft.App

echo.
echo =====================================
echo Extracting version number...
echo =====================================
echo.

set VERSION=0.0.0

for /f "delims=" %%i in ('powershell -Command "(Select-Xml -Path 'DotCraft.App.csproj' -XPath '//Version').Node.InnerText"') do set VERSION=%%i

if "%VERSION%"=="0.0.0" (
    echo Trying alternative version extraction method...
    for /f "tokens=2 delims=>" %%a in ('findstr /C:"<Version>" DotCraft.App.csproj 2^>nul') do (
        for /f "tokens=1 delims=<" %%b in ("%%a") do set VERSION=%%b
    )
)

for /f "tokens=* delims= " %%a in ("%VERSION%") do set VERSION=%%a
echo Version found: %VERSION%

echo.
echo =====================================
echo  Building DotCraft (linux-x64)...
echo =====================================
echo.

call dotnet publish /p:PublishProfile=ReleaseProfile -r linux-x64 -o ..\..\build\linux%MODULE_PROPS%

if %ERRORLEVEL% neq 0 (
    echo Build failed with exit code %ERRORLEVEL%.
    goto :failure
)

goto :package

:failure
echo.
echo Build failed. Please try again.
echo.
pause
exit /b 1

:package
cd ../..

echo.
echo =====================================
echo  Packaging...
echo =====================================
echo.

echo Creating dotcraft-linux-x64_v%VERSION%.tar.gz...
tar -czf "build\dotcraft-linux-x64_v%VERSION%.tar.gz" -C "build\linux" dotcraft

if %ERRORLEVEL% neq 0 (
    echo Packaging failed with exit code %ERRORLEVEL%.
    goto :failure
)

echo.
echo =====================================
echo  Build completed successfully!
echo =====================================
echo  - build\linux\dotcraft
echo  - build\dotcraft-linux-x64_v%VERSION%.tar.gz
echo =====================================
echo.
pause
