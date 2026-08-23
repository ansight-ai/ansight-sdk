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

capacitor_compiled_version="$(extract_first 'COMPILED_CAPACITOR_CORE_VERSION = "([^"]+)"' "${repo_root}/src/capacitor/src/session-properties.ts")"
capacitor_dependency_version="$(extract_first '"@capacitor/core"\s*:\s*"\^([^"]+)"' "${repo_root}/src/capacitor/package.json")"
if [[ "${capacitor_compiled_version}" != "${capacitor_dependency_version}" ]]; then
  fail "Capacitor runtime metadata (${capacitor_compiled_version}) does not match the compiled @capacitor/core dependency (${capacitor_dependency_version})"
fi

add_version \
  "dotnet:Directory.Build.props" \
  "$(extract_first '<AnsightPackageVersion>([^<]+)</AnsightPackageVersion>' "${repo_root}/src/dotnet/Directory.Build.props")"

add_version \
  "android:gradle.properties" \
  "$(sed -n 's/^ansightAndroidVersion=//p' "${repo_root}/src/android/gradle.properties" | head -n 1)"

for gradle_file in "${repo_root}"/src/android/*/build.gradle.kts; do
  android_fallback_version="$(extract_first 'providers\.gradleProperty\("ansightAndroidVersion"\)\.orElse\("([^"]+)"\)' "${gradle_file}")"
  if [[ -n "${android_fallback_version}" ]]; then
    add_version \
      "android:$(basename "$(dirname "${gradle_file}")") fallback" \
      "${android_fallback_version}"
  fi
done

add_version \
  "android:test-app validation default" \
  "$(extract_first 'DEFAULT_ANDROID_SDK_ARTIFACT = "ai\.ansight:ansight-android:([^"]+)"' "${repo_root}/scripts/validate_android_test_apps.py")"

for podspec in "${repo_root}"/src/ios/*.podspec; do
  add_version \
    "ios:$(basename "${podspec}")" \
    "$(extract_first 's\.version\s*=\s*"([^"]+)"' "${podspec}")"
done

add_version \
  "ios:runtime SDK metadata" \
  "$(extract_first 'public static let version = "([^"]+)"' "${repo_root}/src/ios/Sources/AnsightCore/AnsightSDKInfo.swift")"

add_version \
  "react-native:package.json" \
  "$(extract_first '"version"\s*:\s*"([^"]+)"' "${repo_root}/src/react-native/package.json")"

add_version \
  "react-native:android fallback" \
  "$(extract_first 'findProperty\("ansightAndroidVersion"\)\s*\?:\s*"([^"]+)"' "${repo_root}/src/react-native/android/build.gradle")"

add_version \
  "capacitor:package.json" \
  "$(extract_first '"version"\s*:\s*"([^"]+)"' "${repo_root}/src/capacitor/package.json")"

add_version \
  "capacitor:package-lock.json" \
  "$(extract_first '"version"\s*:\s*"([^"]+)"' "${repo_root}/src/capacitor/package-lock.json")"

add_version \
  "capacitor:runtime SDK metadata" \
  "$(extract_first 'ANSIGHT_CAPACITOR_SDK_VERSION = "([^"]+)"' "${repo_root}/src/capacitor/src/session-properties.ts")"

add_version \
  "capacitor:android fallback" \
  "$(extract_first 'findProperty\("ansightAndroidVersion"\)\s*\?:\s*"([^"]+)"' "${repo_root}/src/capacitor/android/build.gradle")"

add_version \
  "capacitor:CocoaPods" \
  "$(extract_first 's\.version\s*=\s*['\''"]([^'\''"]+)['\''"]' "${repo_root}/src/capacitor/AnsightCapacitor.podspec")"

add_version \
  "capacitor:SwiftPM dependency" \
  "$(extract_first 'ansight-sdk\.git",\s*exact:\s*"([^"]+)"' "${repo_root}/src/capacitor/Package.swift")"

add_version \
  "flutter:pubspec.yaml" \
  "$(extract_first '\nversion:\s*([^\s]+)' "${repo_root}/src/flutter/pubspec.yaml")"

add_version \
  "flutter:runtime SDK metadata" \
  "$(extract_first 'ansightFlutterSdkVersion = ['\''"]([^'\''"]+)['\''"]' "${repo_root}/src/flutter/lib/src/session_properties.dart")"

add_version \
  "flutter:Android package" \
  "$(extract_first 'version\s*=\s*"([^"]+)"' "${repo_root}/src/flutter/android/build.gradle")"

add_version \
  "flutter:Android fallback" \
  "$(extract_first 'findProperty\("ansightAndroidVersion"\)\s*\?:\s*"([^"]+)"' "${repo_root}/src/flutter/android/build.gradle")"

add_version \
  "flutter:CocoaPods" \
  "$(extract_first 's\.version\s*=\s*['\''"]([^'\''"]+)['\''"]' "${repo_root}/src/flutter/ios/ansight_flutter.podspec")"

add_version \
  "flutter:SwiftPM dependency" \
  "$(extract_first 'ansight-sdk\.git",\s*exact:\s*"([^"]+)"' "${repo_root}/src/flutter/ios/ansight_flutter/Package.swift")"

add_version \
  "react-native:README CocoaPods" \
  "$(extract_first 'CocoaPods: `Ansight`, `AnsightObjC` version `([^`]+)`' "${repo_root}/src/react-native/README.md")"

add_version \
  "react-native:README Maven" \
  "$(extract_first 'ai\.ansight:ansight-android:([^`]+)' "${repo_root}/src/react-native/README.md")"

add_version \
  "ios-native-harness:published SwiftPM" \
  "$(extract_first 'exactVersion:\s*([0-9A-Za-z.+-]+)' "${repo_root}/src/ios/Examples/NativeHarness/project.published.yml")"

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
