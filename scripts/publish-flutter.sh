#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_root="${repo_root}/src/flutter"
publish=false
skip_tests=false

usage() {
  cat <<'EOF'
Usage: scripts/publish-flutter.sh [options]

Validates the Flutter SDK and optional Pencil package and prepares pub.dev archives. Publishing is
disabled unless --publish is supplied.

Options:
  --publish       Publish ansight_flutter and ansight_pencil to pub.dev.
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
  cmp \
    "${package_root}/ios/ansight_flutter/Sources/ansight_flutter/AnsightPlugin.swift" \
    "${package_root}/macos/ansight_flutter/Sources/ansight_flutter/AnsightPlugin.swift"
  cmp \
    "${package_root}/ios/ansight_flutter/Sources/ansight_flutter/AnsightMessages.g.swift" \
    "${package_root}/macos/ansight_flutter/Sources/ansight_flutter/AnsightMessages.g.swift"
  cmp \
    "${package_root}/ios/ansight_flutter/Sources/ansight_flutter/PrivacyInfo.xcprivacy" \
    "${package_root}/macos/ansight_flutter/Sources/ansight_flutter/PrivacyInfo.xcprivacy"
  (
    cd "${package_root}"
    flutter analyze
    flutter test
  )
  (
    cd "${repo_root}/test-apps/core/flutter"
    flutter test
  )
  "${repo_root}/scripts/validate-flutter-macos.sh"
fi

if [[ "${skip_tests}" != "true" ]]; then
  (
    cd "${repo_root}/src/flutter-pencil"
    flutter pub get
    flutter analyze
    flutter test
  )
fi

for package_root in "${repo_root}/src/flutter" "${repo_root}/src/flutter-pencil"; do

(
  cd "${package_root}"
  dart pub publish --dry-run
)

if [[ "${publish}" == "true" ]]; then
  (
    cd "${package_root}"
    dart pub publish --force
  )
else
  echo "Flutter package validated. Re-run with --publish to upload it."
fi

done
