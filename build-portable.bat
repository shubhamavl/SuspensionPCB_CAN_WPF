@echo off
REM Build script for portable deployment
REM Creates a self-contained, single-file executable
REM NOTE: Self-contained means .NET runtime is INCLUDED - users don't need to install .NET!

echo Building portable version (includes .NET runtime - no installation needed)...
echo.

REM 1) Build main WPF application (portable, single-file)
dotnet publish SuspensionPCB_CAN_WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ========================================
    echo Build failed!
    echo ========================================
    echo.
    exit /b %ERRORLEVEL%
    goto :end
)

REM 2) Build external updater helper (also self-contained single-file)
dotnet publish ..\SuspensionPCB_Updater\SuspensionPCB_Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ========================================
    echo Updater build failed!
    echo ========================================
    echo.
    goto :end
)

REM 3) Copy updater EXE into main publish folder so auto-update can find it
set PUBLISH_DIR=bin\Release\net8.0-windows\win-x64\publish
set UPDATER_PUBLISH=..\SuspensionPCB_Updater\bin\Release\net8.0-windows\win-x64\publish

if exist "%UPDATER_PUBLISH%\SuspensionPCB_Updater.exe" (
    copy /Y "%UPDATER_PUBLISH%\SuspensionPCB_Updater.exe" "%PUBLISH_DIR%\SuspensionPCB_Updater.exe" >nul
)

echo.
echo ========================================
echo Build successful!
echo ========================================
echo.
echo Output location: %PUBLISH_DIR%
echo.
echo The portable executable and updater are ready for distribution.
echo Copy the contents of the publish folder to distribute.
echo.

:end

pause

