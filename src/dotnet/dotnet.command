#!/bin/bash
set -euo pipefail

# Move to the directory where this script resides so relative paths work when double-clicked.
cd "$(dirname "$0")"

# Build and pack the Ansight.Native NuGet package.
dotnet build Ansight.Native/Ansight.Native.csproj -c Release
dotnet pack Ansight.Native/Ansight.Native.csproj -c Release -o ./products
