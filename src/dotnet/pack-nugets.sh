#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

configuration="${1:-Release}"
projects=(
  "Ansight/Ansight.csproj"
  "Ansight.Pairing/Ansight.Pairing.csproj"
  "Ansight.Tools.Database/Ansight.Tools.Database.csproj"
  "Ansight.Tools.FileSystem/Ansight.Tools.FileSystem.csproj"
  "Ansight.Tools.Maui/Ansight.Tools.Maui.csproj"
  "Ansight.Tools.Preferences/Ansight.Tools.Preferences.csproj"
  "Ansight.Tools.Reflection/Ansight.Tools.Reflection.csproj"
  "Ansight.Tools.SecureStorage/Ansight.Tools.SecureStorage.csproj"
  "Ansight.Tools.VisualTree/Ansight.Tools.VisualTree.csproj"
)

for project in "${projects[@]}"; do
  echo "Packing ${project} (${configuration})..."
  dotnet pack "${project}" -c "${configuration}" --nologo -p:GeneratePackageOnBuild=false
done

echo "NuGet packages written to $(pwd)/products"
