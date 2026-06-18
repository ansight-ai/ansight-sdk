#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="Release"
publish=false
skip_build=false

usage() {
  cat <<'EOF'
Usage: scripts/publish-dotnet.sh [--publish] [--configuration Release] [--skip-build]

Default mode builds and packs NuGet packages into src/dotnet/products.
Use --publish to push packages with src/dotnet/upload-nugets.sh.

Environment for --publish:
  ANSIGHT_NUGET_API_KEY   NuGet API key
  NUGET_SOURCE            Optional NuGet source URL
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish)
      publish=true
      shift
      ;;
    --configuration)
      configuration="${2:?missing configuration}"
      shift 2
      ;;
    --skip-build)
      skip_build=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "error: unknown argument '$1'" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet CLI is required" >&2
  exit 1
fi

if [[ "${publish}" == "true" && -z "${ANSIGHT_NUGET_API_KEY:-}" ]]; then
  echo "error: set ANSIGHT_NUGET_API_KEY before publishing NuGet packages" >&2
  exit 1
fi

if [[ "${skip_build}" != "true" ]]; then
  dotnet build "${repo_root}/src/dotnet/Ansight.Sdk.sln" -c "${configuration}" --nologo
fi

if [[ "${publish}" == "true" ]]; then
  (cd "${repo_root}/src/dotnet" && ./upload-nugets.sh "${configuration}")
else
  (cd "${repo_root}/src/dotnet" && ./pack-nugets.sh "${configuration}")
fi
