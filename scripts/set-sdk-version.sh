#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'EOF'
Usage: scripts/set-sdk-version.sh <version>

Updates SDK package metadata for .NET, Android, iOS CocoaPods, React Native,
Capacitor, and Flutter.
The script does not create commits, tags, or publish artifacts.
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

version="${1:-}"

if [[ -z "${version}" ]]; then
  usage >&2
  exit 1
fi

if [[ ! "${version}" =~ ^[0-9]+[.][0-9]+[.][0-9]+([-.+][0-9A-Za-z.-]+)?$ ]]; then
  echo "error: '${version}' is not a SemVer-like SDK version" >&2
  exit 1
fi

if ! command -v node >/dev/null 2>&1; then
  echo "error: node is required to update JavaScript package metadata" >&2
  exit 1
fi

export VERSION="${version}"

perl -0pi -e 's/(<AnsightPackageVersion>).*?(<\/AnsightPackageVersion>)/$1$ENV{VERSION}$2/s' \
  "${repo_root}/src/dotnet/Directory.Build.props"

perl -0pi -e 's/^ansightAndroidVersion=.*/ansightAndroidVersion=$ENV{VERSION}/m' \
  "${repo_root}/src/android/gradle.properties"

for gradle_file in "${repo_root}"/src/android/*/build.gradle.kts; do
  perl -0pi -e 's/(providers\.gradleProperty\("ansightAndroidVersion"\)\.orElse\(")[^"]+("\)\.get\(\))/$1$ENV{VERSION}$2/g' "${gradle_file}"
done

for podspec in "${repo_root}"/src/ios/*.podspec; do
  perl -0pi -e 's/(s\.version\s*=\s*")[^"]+(")/$1$ENV{VERSION}$2/g' "${podspec}"
done

perl -0pi -e 's/(public static let version = ")[^"]+(")/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/ios/Sources/AnsightCore/AnsightSDKInfo.swift"

PACKAGE_JSON="${repo_root}/src/react-native/package.json" node <<'NODE'
const fs = require("fs");

const packageJson = process.env.PACKAGE_JSON;
const version = process.env.VERSION;
const pkg = JSON.parse(fs.readFileSync(packageJson, "utf8"));
pkg.version = version;
fs.writeFileSync(packageJson, `${JSON.stringify(pkg, null, 2)}\n`);
NODE

PACKAGE_JSON="${repo_root}/src/capacitor/package.json" \
PACKAGE_LOCK="${repo_root}/src/capacitor/package-lock.json" node <<'NODE'
const fs = require("fs");

const packageJson = process.env.PACKAGE_JSON;
const packageLock = process.env.PACKAGE_LOCK;
const version = process.env.VERSION;
const pkg = JSON.parse(fs.readFileSync(packageJson, "utf8"));
pkg.version = version;
fs.writeFileSync(packageJson, `${JSON.stringify(pkg, null, 2)}\n`);

const lock = JSON.parse(fs.readFileSync(packageLock, "utf8"));
lock.version = version;
if (lock.packages?.[""]) lock.packages[""].version = version;
fs.writeFileSync(packageLock, `${JSON.stringify(lock, null, 2)}\n`);
NODE

perl -0pi -e 's/(ANSIGHT_CAPACITOR_SDK_VERSION = ")[^"]+(")/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/capacitor/src/session-properties.ts"

perl -0pi -e 's/(ai\.ansight:ansight-android:)[^")]+/$1$ENV{VERSION}/g' \
  "${repo_root}/src/react-native/android/build.gradle"

perl -0pi -e 's/(findProperty\("ansightAndroidVersion"\)\s*\?:\s*")[^"]+(")/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/react-native/android/build.gradle"

perl -0pi -e 's/(ai\.ansight:ansight-android:)[^"]+/$1$ENV{VERSION}/g' \
  "${repo_root}/src/capacitor/android/build.gradle"

perl -0pi -e 's/(findProperty\("ansightAndroidVersion"\)\s*\?:\s*")[^"]+(")/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/capacitor/android/build.gradle"

perl -0pi -e 's/(s\.version\s*=\s*'\''|s\.version\s*=\s*")[^'\''"]+(['\''"])/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/capacitor/AnsightCapacitor.podspec"

perl -0pi -e 's|(ansight-sdk\.git",\s*exact:\s*")[^"]+(")|$1$ENV{VERSION}$2|g' \
  "${repo_root}/src/capacitor/Package.swift"

perl -0pi -e 's/^version:\s*\S+/version: $ENV{VERSION}/m' \
  "${repo_root}/src/flutter/pubspec.yaml"

perl -0pi -e 's/(ansightFlutterSdkVersion = '\''|ansightFlutterSdkVersion = ")[^'\''"]+(['\''"])/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/flutter/lib/src/session_properties.dart"

perl -0pi -e 's/^(version\s*=\s*")[^"]+(")/$1$ENV{VERSION}$2/m; s/(ai\.ansight:ansight-android:)[^"]+/$1$ENV{VERSION}/g' \
  "${repo_root}/src/flutter/android/build.gradle"

perl -0pi -e 's/(findProperty\("ansightAndroidVersion"\)\s*\?:\s*")[^"]+(")/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/flutter/android/build.gradle"

perl -0pi -e 's/(s\.version\s*=\s*'\''|s\.version\s*=\s*")[^'\''"]+(['\''"])/$1$ENV{VERSION}$2/g' \
  "${repo_root}/src/flutter/ios/ansight_flutter.podspec"

perl -0pi -e 's|(ansight-sdk\.git",\s*exact:\s*")[^"]+(")|$1$ENV{VERSION}$2|g' \
  "${repo_root}/src/flutter/ios/ansight_flutter/Package.swift"

perl -0pi -e 's/(ansight_flutter:\s*\^)[0-9A-Za-z.+-]+/$1$ENV{VERSION}/g' \
  "${repo_root}/src/flutter/README.md"

perl -0pi -e 's/(ai\.ansight:[A-Za-z0-9_.-]+:)[0-9][0-9A-Za-z.+-]*/$1$ENV{VERSION}/g' \
  "${repo_root}/src/android/README.md"

perl -0pi -e 's/(DEFAULT_ANDROID_SDK_ARTIFACT = "ai\.ansight:ansight-android:)[^"]+(")/$1$ENV{VERSION}$2/g' \
  "${repo_root}/scripts/validate_android_test_apps.py"

perl -0pi -e 's/(exactVersion:\s*)[0-9A-Za-z.+-]+/$1$ENV{VERSION}/g' \
  "${repo_root}/src/ios/Examples/NativeHarness/project.published.yml"

perl -0pi -e 's/(CocoaPods: `Ansight`, `AnsightObjC` version `)[^`]+(`)/$1$ENV{VERSION}$2/g; s/(ai\.ansight:ansight-android:)[^`]+/$1$ENV{VERSION}/g' \
  "${repo_root}/src/react-native/README.md"

"${repo_root}/scripts/check-sdk-versions.sh"
