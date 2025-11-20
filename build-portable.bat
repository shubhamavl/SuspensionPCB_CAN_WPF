@echo off
REM Build script for portable deployment
REM Creates a self-contained, single-file executable
REM NOTE: Self-contained means .NET runtime is INCLUDED - users don't need to install .NET!

echo Building portable version (includes .NET runtime - no installation needed)...
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo Build successful!
    echo ========================================
    echo.
    echo Output location: bin\Release\net8.0-windows\win-x64\publish\
    echo.
    echo The portable executable is ready for distribution.
    echo Copy the contents of the publish folder to distribute.
    echo.
) else (
    echo.
    echo ========================================
    echo Build failed!
    echo ========================================
    echo.
    exit /b %ERRORLEVEL%
)

pause

