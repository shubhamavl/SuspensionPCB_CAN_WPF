#!/bin/bash
# Build script for portable deployment (Linux/Mac)
# Creates a self-contained, single-file executable

echo "Building portable version..."
echo ""

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true

if [ $? -eq 0 ]; then
    echo ""
    echo "========================================"
    echo "Build successful!"
    echo "========================================"
    echo ""
    echo "Output location: bin/Release/net8.0-windows/win-x64/publish/"
    echo ""
    echo "The portable executable is ready for distribution."
    echo "Copy the contents of the publish folder to distribute."
    echo ""
else
    echo ""
    echo "========================================"
    echo "Build failed!"
    echo "========================================"
    echo ""
    exit 1
fi

