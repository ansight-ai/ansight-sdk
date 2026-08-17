#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_root="${repo_root}/src/flutter"
location_package_root="${repo_root}/src/flutter-location"
publish=false
skip_tests=false

usage() {
  cat <<'EOF'
Usage: scripts/publish-flutter.sh [options]

Validates the Flutter package and prepares a pub.dev archive. Publishing is
disabled unless --publish is supplied.

Options:
  --publish       Publish ansight_flutter to pub.dev.
  --skip-tests    Skip analyze and test commands.
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

if ! command -v flutter >/dev/null 2>&1; then
  echo "error: flutter is required" >&2
  exit 1
fi

if [[ "${skip_tests}" != "true" ]]; then
  (
    cd "${package_root}"
    flutter analyze
    flutter test
  )
  (
    cd "${package_root}/example"
    flutter test
  )
fi

(
  cd "${package_root}"
  dart pub publish --dry-run
)

location_override="${location_package_root}/pubspec_overrides.yaml"
if [[ -e "${location_override}" ]]; then
  echo "error: ${location_override} already exists; remove it before publishing" >&2
  exit 1
fi

cleanup_location_override() {
  rm -f "${location_override}"
}
trap cleanup_location_override EXIT
printf 'dependency_overrides:\n  ansight_flutter:\n    path: ../flutter\n' > "${location_override}"

(
  cd "${location_package_root}"
  flutter pub get
  if [[ "${skip_tests}" != "true" ]]; then
    flutter analyze
    flutter test
  fi
  dart pub publish --dry-run
)

cleanup_location_override
trap - EXIT

if [[ "${publish}" == "true" ]]; then
  (
    cd "${package_root}"
    dart pub publish --force
  )
  (
    cd "${location_package_root}"
    flutter pub get
    dart pub publish --force
  )
else
  echo "Flutter package validated. Re-run with --publish to upload it."
fi
