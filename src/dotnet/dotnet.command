#!/bin/bash
set -euo pipefail

# Move to the directory where this script resides so relative paths work when double-clicked.
cd "$(dirname "$0")"

# Build and pack the Ansight NuGet package.
dotnet build Ansight/Ansight.csproj -c Release
dotnet pack Ansight/Ansight.csproj -c Release -o ./products
