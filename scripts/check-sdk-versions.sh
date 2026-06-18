#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
expected_version="${ANSIGHT_EXPECTED_VERSION:-}"

labels=()
versions=()

fail() {
  echo "error: $*" >&2
  exit 1
}

extract_first() {
  local pattern="$1"
  local file="$2"

  PATTERN="${pattern}" perl -0ne 'print "$1\n" if /$ENV{PATTERN}/' "${file}" | head -n 1
}

add_version() {
  local label="$1"
  local version="$2"

  if [[ -z "${version}" ]]; then
    fail "could not read version for ${label}"
  fi

  labels+=("${label}")
  versions+=("${version}")
}

add_version \
  "dotnet:Directory.Build.props" \
  "$(extract_first '<AnsightPackageVersion>([^<]+)</AnsightPackageVersion>' "${repo_root}/src/dotnet/Directory.Build.props")"

add_version \
  "android:gradle.properties" \
  "$(sed -n 's/^ansightAndroidVersion=//p' "${repo_root}/src/android/gradle.properties" | head -n 1)"

for podspec in "${repo_root}"/src/ios/*.podspec; do
  add_version \
    "ios:$(basename "${podspec}")" \
    "$(extract_first 's\.version\s*=\s*"([^"]+)"' "${podspec}")"
done

add_version \
  "react-native:package.json" \
  "$(extract_first '"version"\s*:\s*"([^"]+)"' "${repo_root}/src/react-native/package.json")"

add_version \
  "react-native:android dependency" \
  "$(extract_first 'ai\.ansight:ansight-android:([^")]+)' "${repo_root}/src/react-native/android/build.gradle")"

add_version \
  "react-native:README CocoaPods" \
  "$(extract_first 'CocoaPods: `Ansight`, `AnsightObjC` version `([^`]+)`' "${repo_root}/src/react-native/README.md")"

add_version \
  "react-native:README Maven" \
  "$(extract_first 'ai\.ansight:ansight-android:([^`]+)' "${repo_root}/src/react-native/README.md")"

printf '%-42s %s\n' "Surface" "Version"
printf '%-42s %s\n' "-------" "-------"

for i in "${!labels[@]}"; do
  printf '%-42s %s\n' "${labels[$i]}" "${versions[$i]}"
done

unique_versions="$(printf '%s\n' "${versions[@]}" | LC_ALL=C sort -u)"
unique_count="$(printf '%s\n' "${unique_versions}" | sed '/^$/d' | wc -l | tr -d ' ')"

if [[ -n "${expected_version}" ]]; then
  mismatches=0
  for version in "${versions[@]}"; do
    if [[ "${version}" != "${expected_version}" ]]; then
      mismatches=$((mismatches + 1))
    fi
  done

  if (( mismatches > 0 )); then
    echo
    echo "Expected version: ${expected_version}" >&2
    fail "${mismatches} version value(s) do not match ANSIGHT_EXPECTED_VERSION"
  fi
fi

if [[ "${unique_count}" != "1" ]]; then
  echo
  echo "Found version mismatch:" >&2
  printf '%s\n' "${unique_versions}" >&2
  exit 1
fi

echo
echo "All SDK package metadata is aligned at ${versions[0]}."
