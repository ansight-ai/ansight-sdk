#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${ANSIGHT_HARNESS_VERSION:-}"
skip_cocoapods=false
skip_swiftpm_tag=false

usage() {
  cat <<'EOF'
Usage: scripts/validate-published-sdk-packages.sh [options]

Checks that the published SDK package versions are visible from public package
registries and that the SwiftPM release tag is consumable from the repository
root.

Options:
  --version VERSION       Version to validate. Defaults to src/react-native/package.json.
  --skip-cocoapods        Skip CocoaPods trunk checks.
  --skip-swiftpm-tag      Skip the Git tag root Package.swift check.

Environment:
  ANSIGHT_HARNESS_VERSION Default version to validate.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      version="${2:?missing version}"
      shift 2
      ;;
    --skip-cocoapods)
      skip_cocoapods=true
      shift
      ;;
    --skip-swiftpm-tag)
      skip_swiftpm_tag=true
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

if [[ -z "${version}" ]]; then
  if ! command -v node >/dev/null 2>&1; then
    echo "error: node is required when --version is omitted" >&2
    exit 1
  fi
  version="$(node -p "require('${repo_root}/src/react-native/package.json').version")"
fi

failures=0

check() {
  local label="$1"
  shift

  printf '%-58s' "${label}"
  if "$@" >/tmp/ansight-validation-check.log 2>&1; then
    printf 'OK\n'
  else
    printf 'FAIL\n'
    sed 's/^/  /' /tmp/ansight-validation-check.log >&2
    failures=$((failures + 1))
  fi
}

check_url() {
  local url="$1"
  curl -fsSL --retry 2 --retry-delay 2 "${url}" >/dev/null
}

check_maven_artifact() {
  local artifact_id="$1"
  check \
    "Maven Central ${artifact_id}" \
    check_url "https://repo1.maven.org/maven2/ai/ansight/${artifact_id}/${version}/${artifact_id}-${version}.pom"
}

check_npm_package() {
  local resolved_version
  resolved_version="$(npm view "@ansight/react-native@${version}" version --silent)"
  [[ "${resolved_version}" == "${version}" ]]
}

check_podspec() {
  local pod_name="$1"
  pod spec cat "${pod_name}" --version="${version}" >/dev/null
}

check_swiftpm_root_manifest() {
  swift package --package-path "${repo_root}" describe >/dev/null
}

check_swiftpm_tag_manifest() {
  git -C "${repo_root}" show "v${version}:Package.swift" >/dev/null
}

echo "Validating published Ansight SDK packages at ${version}"
echo

for artifact in \
  ansight-core-android \
  ansight-pairing-android \
  ansight-tools-visualtree-android \
  ansight-tools-filedescriptordiagnostics-android \
  ansight-tools-filesystem-android \
  ansight-tools-preferences-android \
  ansight-tools-securestorage-android \
  ansight-tools-database-android \
  ansight-tools-reflection-android \
  ansight-android; do
  check_maven_artifact "${artifact}"
done

check "npm @ansight/react-native" check_npm_package

if [[ "${skip_cocoapods}" != "true" ]]; then
  if command -v pod >/dev/null 2>&1; then
    for pod_name in \
      AnsightCore \
      AnsightPairingQR \
      AnsightToolsPreferences \
      AnsightToolsFileDescriptorDiagnostics \
      AnsightToolsFileSystem \
      AnsightToolsDatabase \
      AnsightToolsReflection \
      AnsightToolsSecureStorage \
      AnsightToolsVisualTree \
      Ansight \
      AnsightObjC; do
      check "CocoaPods ${pod_name}" check_podspec "${pod_name}"
    done
  else
    echo "Skipping CocoaPods checks because pod is not installed."
  fi
fi

check "SwiftPM current checkout root Package.swift" check_swiftpm_root_manifest

if [[ "${skip_swiftpm_tag}" != "true" ]]; then
  check "SwiftPM tag v${version} root Package.swift" check_swiftpm_tag_manifest
fi

rm -f /tmp/ansight-validation-check.log

if (( failures > 0 )); then
  echo
  echo "${failures} published package validation check(s) failed." >&2
  exit 1
fi

echo
echo "All published package validation checks passed."
