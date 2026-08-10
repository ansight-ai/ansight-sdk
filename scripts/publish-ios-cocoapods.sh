#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/scripts/load-publishing-env.sh"
load_publishing_env "${repo_root}"

release_dir="${repo_root}/build/cocoapods-release-specs"
push=false
generate_only=false
skip_lint=false
allow_warnings=true
spec_repo="${ANSIGHT_COCOAPODS_REPO:-}"
sources="${ANSIGHT_COCOAPODS_SOURCES:-}"

native_podspecs=(
  "AnsightCore.podspec"
  "AnsightPairingQR.podspec"
  "AnsightToolsDatabase.podspec"
  "AnsightToolsFileDescriptorDiagnostics.podspec"
  "AnsightToolsFileSystem.podspec"
  "AnsightToolsPreferences.podspec"
  "AnsightToolsReflection.podspec"
  "AnsightToolsSecureStorage.podspec"
  "AnsightToolsVisualTree.podspec"
  "Ansight.podspec"
  "AnsightObjC.podspec"
)

version="$(
  sed -nE 's/^[[:space:]]*s\.version[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' \
    "${repo_root}/src/ios/AnsightCore.podspec" |
    head -n 1
)"
expected_source_tag="v${version}"
source_tag="${ANSIGHT_POD_SOURCE_TAG:-${expected_source_tag}}"

if [[ "${source_tag}" != "${expected_source_tag}" ]]; then
  echo "error: ANSIGHT_POD_SOURCE_TAG is '${source_tag}', expected '${expected_source_tag}'" >&2
  echo "error: refusing to generate or publish CocoaPods metadata from the wrong Git tag" >&2
  exit 1
fi

export ANSIGHT_POD_SOURCE_TAG="${source_tag}"

usage() {
  cat <<'EOF'
Usage: scripts/publish-ios-cocoapods.sh [options]

Generates release podspecs that point at the Git tag source. Default mode
generates and lints the specs. Use --push for CocoaPods trunk/private repo.

Options:
  --push             Push generated podspecs
  --repo NAME        Use `pod repo push NAME` instead of `pod trunk push`
  --sources LIST     Forward CocoaPods --sources for lint/push
  --skip-lint        Skip pod spec lint in non-push mode
  --generate-only    Generate release podspecs and stop

Environment:
  ANSIGHT_POD_SOURCE_GIT    Default: https://github.com/ansight-ai/ansight-sdk.git
  ANSIGHT_POD_SOURCE_TAG    Default in generated specs: v#{s.version}
  ANSIGHT_COCOAPODS_REPO    Private specs repo name
  ANSIGHT_COCOAPODS_SOURCES Optional CocoaPods sources list
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --push)
      push=true
      shift
      ;;
    --repo)
      spec_repo="${2:?missing repo name}"
      shift 2
      ;;
    --sources)
      sources="${2:?missing sources list}"
      shift 2
      ;;
    --skip-lint)
      skip_lint=true
      shift
      ;;
    --generate-only)
      generate_only=true
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

rm -rf "${release_dir}"
mkdir -p "${release_dir}"

for podspec in "${native_podspecs[@]}"; do
  source_spec="${repo_root}/src/ios/${podspec}"
  target_spec="${release_dir}/${podspec}"

  cp "${source_spec}" "${target_spec}"

  perl -0pi -e '
    s!s\.source\s*=\s*\{\s*:path\s*=>\s*"\."\s*\}!source_git = ENV.fetch("ANSIGHT_POD_SOURCE_GIT", "https://github.com/ansight-ai/ansight-sdk.git")\n  source_tag = ENV.fetch("ANSIGHT_POD_SOURCE_TAG", "v#{s.version}")\n  s.source       = { :git => source_git, :tag => source_tag }!g;
    s!:file => "LICENSE"!:file => "src/ios/LICENSE"!g;
    s!"Sources/!"src/ios/Sources/!g;
    s!"Generated/!"src/ios/Generated/!g;
    s!"Plugins/!"src/ios/Plugins/!g;
    s!\$\{PODS_TARGET_SRCROOT\}/Generated/!\${PODS_TARGET_SRCROOT}/src/ios/Generated/!g;
    s!\$\{PODS_TARGET_SRCROOT\}/Plugins/!\${PODS_TARGET_SRCROOT}/src/ios/Plugins/!g;
    s!\$\{PODS_TARGET_SRCROOT\}/Sources/!\${PODS_TARGET_SRCROOT}/src/ios/Sources/!g;
    s!--package-directory "\$\{PODS_TARGET_SRCROOT\}"!--package-directory "\${PODS_TARGET_SRCROOT}/src/ios"!g;
  ' "${target_spec}"
done

echo "Generated release podspecs in ${release_dir}"

if [[ "${generate_only}" == "true" ]]; then
  exit 0
fi

if ! command -v pod >/dev/null 2>&1; then
  echo "error: CocoaPods is required for lint/push; generated specs remain in ${release_dir}" >&2
  exit 1
fi

common_args=()
if [[ "${allow_warnings}" == "true" ]]; then
  common_args+=(--allow-warnings)
fi
if [[ -n "${sources}" ]]; then
  common_args+=(--sources="${sources}")
fi

for podspec in "${native_podspecs[@]}"; do
  generated_spec="${release_dir}/${podspec}"

  if [[ "${push}" == "true" ]]; then
    if [[ -n "${spec_repo}" ]]; then
      pod repo push "${spec_repo}" "${generated_spec}" "${common_args[@]+"${common_args[@]}"}"
    else
      pod trunk push "${generated_spec}" --synchronous "${common_args[@]+"${common_args[@]}"}"
    fi
  elif [[ "${skip_lint}" != "true" ]]; then
    pod spec lint "${generated_spec}" "${common_args[@]+"${common_args[@]}"}"
  fi
done
