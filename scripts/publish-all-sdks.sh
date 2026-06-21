#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/scripts/load-publishing-env.sh"
load_publishing_env "${repo_root}"

publish=false
skip_tests=false
skip_dotnet=false
skip_android=false
skip_ios_swiftpm=false
skip_ios_cocoapods=false
skip_react_native=false
android_target=""

usage() {
  cat <<'EOF'
Usage: scripts/publish-all-sdks.sh [options]

Default mode checks version alignment and prepares all SDK artifacts without
publishing to public registries.

Options:
  --publish                 Publish to configured registries
  --skip-tests              Forward test-skip flags where supported
  --skip-dotnet
  --skip-android
  --skip-ios-swiftpm
  --skip-ios-cocoapods
  --skip-react-native
  --android-remote          Publish Android to ANSIGHT_MAVEN_URL
  --android-central         Publish Android through the Sonatype Central Portal

Before --publish:
  1. Run scripts/set-sdk-version.sh <version> if needed.
  2. Commit release metadata.
  3. Ensure credentials are exported for each registry.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish)
      publish=true
      shift
      ;;
    --skip-tests)
      skip_tests=true
      shift
      ;;
    --skip-dotnet)
      skip_dotnet=true
      shift
      ;;
    --skip-android)
      skip_android=true
      shift
      ;;
    --skip-ios-swiftpm)
      skip_ios_swiftpm=true
      shift
      ;;
    --skip-ios-cocoapods)
      skip_ios_cocoapods=true
      shift
      ;;
    --skip-react-native)
      skip_react_native=true
      shift
      ;;
    --android-remote)
      android_target="remote"
      shift
      ;;
    --android-central)
      android_target="central"
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

"${repo_root}/scripts/check-sdk-versions.sh"

if [[ "${publish}" == "true" ]]; then
  if ! git -C "${repo_root}" diff --quiet || ! git -C "${repo_root}" diff --cached --quiet; then
    echo "error: refusing to publish from a dirty worktree" >&2
    exit 1
  fi

  if [[ "${skip_android}" != "true" && -z "${android_target}" ]]; then
    echo "error: choose --android-central or --android-remote for --publish" >&2
    exit 1
  fi
fi

if [[ "${skip_ios_swiftpm}" != "true" ]]; then
  swiftpm_args=()
  if [[ "${skip_tests}" == "true" ]]; then
    swiftpm_args+=(--skip-tests)
  fi
  if [[ "${publish}" == "true" ]]; then
    swiftpm_args+=(--push-tag)
  fi
  "${repo_root}/scripts/publish-ios-swiftpm.sh" "${swiftpm_args[@]+"${swiftpm_args[@]}"}"
fi

if [[ "${skip_dotnet}" != "true" ]]; then
  dotnet_args=()
  if [[ "${publish}" == "true" ]]; then
    dotnet_args+=(--publish)
  fi
  "${repo_root}/scripts/publish-dotnet.sh" "${dotnet_args[@]+"${dotnet_args[@]}"}"
fi

if [[ "${skip_android}" != "true" ]]; then
  android_args=()
  if [[ "${skip_tests}" == "true" ]]; then
    android_args+=(--skip-tests)
  fi
  if [[ "${publish}" == "true" ]]; then
    case "${android_target}" in
      central)
        android_args+=(--publish-central)
        ;;
      remote)
        android_args+=(--publish)
        ;;
    esac
  elif [[ "${android_target}" == "central" ]]; then
    android_args+=(--central-bundle)
  fi
  "${repo_root}/scripts/publish-android.sh" "${android_args[@]+"${android_args[@]}"}"
fi

if [[ "${skip_ios_cocoapods}" != "true" ]]; then
  cocoapods_args=()
  if [[ "${publish}" == "true" ]]; then
    cocoapods_args+=(--push)
  else
    cocoapods_args+=(--generate-only)
  fi
  "${repo_root}/scripts/publish-ios-cocoapods.sh" "${cocoapods_args[@]+"${cocoapods_args[@]}"}"
fi

if [[ "${skip_react_native}" != "true" ]]; then
  rn_args=()
  if [[ "${publish}" == "true" ]]; then
    rn_args+=(--publish)
  fi
  "${repo_root}/scripts/publish-react-native.sh" "${rn_args[@]+"${rn_args[@]}"}"
fi
