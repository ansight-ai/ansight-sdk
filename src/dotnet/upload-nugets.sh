#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

source_url="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
api_key="${ANSIGHT_NUGET_API_KEY:-}"
configuration="${1:-Release}"
package_version="${NUGET_PACKAGE_VERSION:-}"

if [[ -z "${api_key}" ]]; then
  echo "Set ANSIGHT_NUGET_API_KEY before uploading." >&2
  exit 1
fi

./pack-nugets.sh "${configuration}"

shopt -s nullglob

if [[ -z "${package_version}" ]]; then
  package_version="$(dotnet msbuild Ansight.Core/Ansight.Core.csproj -nologo -getProperty:AnsightPackageVersion)"
fi

packages=(products/*.${package_version}.nupkg)

if (( ${#packages[@]} == 0 )); then
  echo "No packages found in $(pwd)/products for version ${package_version}. Run ./pack-nugets.sh first." >&2
  exit 1
fi

for package in "${packages[@]}"; do
  echo "Uploading ${package}..."
  dotnet nuget push "${package}" \
    --api-key "${api_key}" \
    --source "${source_url}" \
    --skip-duplicate
done
