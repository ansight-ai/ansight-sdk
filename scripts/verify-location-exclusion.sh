#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

fail() {
  echo "location exclusion check failed: $1" >&2
  exit 1
}

reject_reference() {
  local file="$1"
  local pattern="$2"
  local description="$3"
  if grep -Eiq "${pattern}" "${file}"; then
    fail "${description} references the optional location module"
  fi
}

reject_reference "${repo_root}/src/dotnet/Ansight/Ansight.csproj" 'Ansight[.]Location' '.NET aggregate'
reject_reference "${repo_root}/src/dotnet/Ansight.Maui/Ansight.Maui.csproj" 'Ansight[.]Location' '.NET MAUI aggregate'
reject_reference "${repo_root}/src/android/ansight/build.gradle.kts" 'ansight-location' 'Android aggregate'
reject_reference "${repo_root}/src/react-native/package.json" 'react-native-location' 'React Native base package'
reject_reference "${repo_root}/src/capacitor/package.json" 'capacitor-location' 'Capacitor base package'
reject_reference "${repo_root}/src/flutter/pubspec.yaml" 'ansight_location' 'Flutter base package'

for manifest in \
  "${repo_root}/src/android/ansight-location/src/main/AndroidManifest.xml" \
  "${repo_root}/src/react-native/android/src/main/AndroidManifest.xml" \
  "${repo_root}/src/capacitor/android/src/main/AndroidManifest.xml" \
  "${repo_root}/src/flutter/android/src/main/AndroidManifest.xml"; do
  reject_reference "${manifest}" 'ACCESS_(FINE|COARSE|BACKGROUND)_LOCATION' "$(basename "${manifest}")"
done

swift_ansight_dependencies="$(awk '
  /^    targets: \[/ { targets_section = 1 }
  targets_section && /name: "Ansight",/ { in_target = 1 }
  in_target { print }
  in_target && /path: "Sources\/Ansight",/ { exit }
' "${repo_root}/src/ios/Package.swift")"
if grep -q 'AnsightLocation' <<<"${swift_ansight_dependencies}"; then
  fail 'Swift Ansight aggregate depends on AnsightLocation'
fi

echo 'Location exclusion boundaries verified for every SDK surface.'
