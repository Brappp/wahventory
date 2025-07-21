#!/bin/bash
echo "Building wahventory..."
dotnet build wahventory.sln --configuration Release
echo ""
echo "Build complete! Plugin DLL location:"
echo "wahventory/bin/x64/Release/wahventory.dll"
