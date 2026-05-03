#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

configuration="${1:-Release}"
solution="Ansight.Sdk.sln"
props_file="Directory.Build.props"
source_url="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

confirm() {
  local prompt="$1"
  local reply

  read -r -p "${prompt} [y/N] " reply
  case "${reply}" in
    y|Y|yes|YES|Yes) return 0 ;;
    *) return 1 ;;
  esac
}

current_version() {
  dotnet msbuild Ansight.Core/Ansight.Core.csproj -nologo -getProperty:AnsightPackageVersion
}

bump_pre_version() {
  local version="$1"

  if [[ "${version}" =~ ^(.+-pre)([0-9]+)$ ]]; then
    local prefix="${BASH_REMATCH[1]}"
    local number="${BASH_REMATCH[2]}"
    printf '%s%s\n' "${prefix}" "$((10#${number} + 1))"
    return 0
  fi

  return 1
}

update_version() {
  local version="$1"

  perl -0pi -e "s#<AnsightPackageVersion>.*?</AnsightPackageVersion>#<AnsightPackageVersion>${version}</AnsightPackageVersion>#" "${props_file}"
}

version="$(current_version)"

if next_version="$(bump_pre_version "${version}")"; then
  if confirm "Bump NuGet package version from ${version} to ${next_version}?"; then
    update_version "${next_version}"
    version="${next_version}"
  fi
else
  echo "Current NuGet package version is ${version}."
  if confirm "Enter a new NuGet package version?"; then
    read -r -p "New version: " next_version
    if [[ -z "${next_version}" ]]; then
      echo "Version cannot be empty." >&2
      exit 1
    fi

    update_version "${next_version}"
    version="${next_version}"
  fi
fi

echo "Building ${solution} (${configuration})..."
dotnet build "${solution}" -c "${configuration}" --nologo

echo "Packing NuGet packages for ${version}..."
./pack-nugets.sh "${configuration}"

shopt -s nullglob
packages=(products/*.${version}.nupkg)

if (( ${#packages[@]} == 0 )); then
  echo "No packages found in $(pwd)/products for version ${version}." >&2
  exit 1
fi

echo
echo "Prepared ${#packages[@]} package(s):"
for package in "${packages[@]}"; do
  echo "  ${package}"
done

echo
if ! confirm "Upload these packages to ${source_url}?"; then
  echo "Upload skipped."
  exit 0
fi

api_key="${ANSIGHT_NUGET_API_KEY:-}"
if [[ -z "${api_key}" ]]; then
  echo "Set ANSIGHT_NUGET_API_KEY before uploading." >&2
  exit 1
fi

for package in "${packages[@]}"; do
  echo "Uploading ${package}..."
  dotnet nuget push "${package}" \
    --api-key "${api_key}" \
    --source "${source_url}" \
    --skip-duplicate
done

echo "Upload complete."
