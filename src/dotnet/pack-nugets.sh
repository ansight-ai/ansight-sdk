#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

configuration="${1:-Release}"
pack_mode="${2:-}"
products_dir="$(pwd)/products"
projects=(
  "Ansight.Native.Android.Binding/Ansight.Native.Android.Binding.csproj"
  "Ansight.Native.Apple.Binding/Ansight.Native.Apple.Binding.csproj"
  "Ansight.Core/Ansight.Core.csproj"
  "Ansight.Annotations/Ansight.Annotations.csproj"
  "Ansight.OfflineCapture/Ansight.OfflineCapture.csproj"
  "Ansight.Pairing/Ansight.Pairing.csproj"
  "Ansight.Tools.Database/Ansight.Tools.Database.csproj"
  "Ansight.Tools.FileSystem/Ansight.Tools.FileSystem.csproj"
  "Ansight.Tools.Preferences/Ansight.Tools.Preferences.csproj"
  "Ansight.Tools.Reflection/Ansight.Tools.Reflection.csproj"
  "Ansight.Tools.SecureStorage/Ansight.Tools.SecureStorage.csproj"
  "Ansight.Tools.VisualTree/Ansight.Tools.VisualTree.csproj"
  "Ansight/Ansight.csproj"
  "Ansight.Location/Ansight.Location.csproj"
  "Ansight.Location.Maui/Ansight.Location.Maui.csproj"
  "Ansight.Tools.Maui/Ansight.Tools.Maui.csproj"
  "Ansight.Maui/Ansight.Maui.csproj"
)

pack_args=()
if [[ "${pack_mode}" == "--no-build" ]]; then
  pack_args+=(--no-build)
elif [[ -n "${pack_mode}" ]]; then
  echo "error: unknown argument '${pack_mode}'" >&2
  exit 1
fi

echo "Cleaning ${products_dir}..."
rm -rf "${products_dir}"
mkdir -p "${products_dir}"

for project in "${projects[@]}"; do
  echo "Packing ${project} (${configuration})..."
  dotnet pack "${project}" -c "${configuration}" --nologo -p:GeneratePackageOnBuild=false -p:BuildInParallel=false -maxcpucount:1 "${pack_args[@]+"${pack_args[@]}"}"
done

echo "NuGet packages written to $(pwd)/products"
