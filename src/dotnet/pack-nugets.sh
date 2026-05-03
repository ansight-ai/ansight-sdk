#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

configuration="${1:-Release}"
products_dir="$(pwd)/products"
projects=(
  "Ansight.Core/Ansight.Core.csproj"
  "Ansight.Pairing/Ansight.Pairing.csproj"
  "Ansight.Tools.Database/Ansight.Tools.Database.csproj"
  "Ansight.Tools.FileSystem/Ansight.Tools.FileSystem.csproj"
  "Ansight.Tools.Preferences/Ansight.Tools.Preferences.csproj"
  "Ansight.Tools.Reflection/Ansight.Tools.Reflection.csproj"
  "Ansight.Tools.SecureStorage/Ansight.Tools.SecureStorage.csproj"
  "Ansight.Tools.VisualTree/Ansight.Tools.VisualTree.csproj"
  "Ansight/Ansight.csproj"
  "Ansight.Tools.Maui/Ansight.Tools.Maui.csproj"
  "Ansight.Maui/Ansight.Maui.csproj"
)

echo "Cleaning ${products_dir}..."
rm -rf "${products_dir}"
mkdir -p "${products_dir}"

for project in "${projects[@]}"; do
  echo "Packing ${project} (${configuration})..."
  dotnet pack "${project}" -c "${configuration}" --nologo -p:GeneratePackageOnBuild=false -p:BuildInParallel=false -maxcpucount:1
done

echo "NuGet packages written to $(pwd)/products"
