#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_mode="${ANSIGHT_RN_HARNESS_PACKAGE_SOURCE:-local}"
version="${ANSIGHT_HARNESS_VERSION:-}"
harness_path="${ANSIGHT_REACT_NATIVE_HARNESS_PATH:-${repo_root}/../ansight-sdk-test-apps/react-native/ansight-react-native-harness}"
run_install=false

usage() {
  cat <<'EOF'
Usage: scripts/configure-react-native-harness-source.sh [options]

Configures the first-party React Native harness to consume either the local
SDK checkout or the published package set.

Options:
  --source local|published    Package source to use. Defaults to local.
  --version VERSION           Published package version. Defaults to src/react-native/package.json.
  --harness-path PATH         Harness path. Defaults to ../ansight-sdk-test-apps/react-native/ansight-react-native-harness.
  --install                   Run npm install in the harness after updating package.json.

Environment:
  ANSIGHT_RN_HARNESS_PACKAGE_SOURCE  Default source mode.
  ANSIGHT_HARNESS_VERSION            Default published package version.
  ANSIGHT_REACT_NATIVE_HARNESS_PATH  Default harness path.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source)
      source_mode="${2:?missing source}"
      shift 2
      ;;
    --version)
      version="${2:?missing version}"
      shift 2
      ;;
    --harness-path)
      harness_path="${2:?missing harness path}"
      shift 2
      ;;
    --install)
      run_install=true
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

case "${source_mode}" in
  local|published)
    ;;
  *)
    echo "error: --source must be 'local' or 'published'" >&2
    exit 1
    ;;
esac

if ! command -v node >/dev/null 2>&1; then
  echo "error: node is required" >&2
  exit 1
fi

if [[ -z "${version}" ]]; then
  version="$(node -p "require('${repo_root}/src/react-native/package.json').version")"
fi

if [[ ! -f "${harness_path}/package.json" ]]; then
  echo "error: React Native harness package.json not found at ${harness_path}" >&2
  exit 1
fi

export ANSIGHT_REPO_ROOT="${repo_root}"
export ANSIGHT_RN_HARNESS_PATH="${harness_path}"
export ANSIGHT_RN_HARNESS_PACKAGE_SOURCE="${source_mode}"
export ANSIGHT_RN_HARNESS_VERSION="${version}"

node <<'NODE'
const fs = require("fs");
const path = require("path");

const repoRoot = process.env.ANSIGHT_REPO_ROOT;
const harnessPath = process.env.ANSIGHT_RN_HARNESS_PATH;
const sourceMode = process.env.ANSIGHT_RN_HARNESS_PACKAGE_SOURCE;
const version = process.env.ANSIGHT_RN_HARNESS_VERSION;

function read(file) {
  return fs.readFileSync(file, "utf8");
}

function write(file, content) {
  fs.writeFileSync(file, content);
}

function rubySingleQuoted(value) {
  return value.replace(/\\/g, "\\\\").replace(/'/g, "\\'");
}

const packageJsonPath = path.join(harnessPath, "package.json");
const packageJson = JSON.parse(read(packageJsonPath));
packageJson.dependencies ||= {};
packageJson.dependencies["@ansight/react-native"] =
  sourceMode === "local"
    ? `file:${path.join(repoRoot, "src/react-native")}`
    : version;
write(packageJsonPath, `${JSON.stringify(packageJson, null, 2)}\n`);

const podfilePath = path.join(harnessPath, "ios/Podfile");
if (fs.existsSync(podfilePath)) {
  const podNames = [
    "AnsightCore",
    "AnsightPairingQR",
    "AnsightToolsPreferences",
    "AnsightToolsFileSystem",
    "AnsightToolsDatabase",
    "AnsightToolsFileDescriptorDiagnostics",
    "AnsightToolsReflection",
    "AnsightToolsSecureStorage",
    "AnsightToolsVisualTree",
    "Ansight",
    "AnsightObjC",
  ];
  const iosPath = rubySingleQuoted(path.join(repoRoot, "src/ios"));
  const localPods = podNames
    .map((podName) => `    pod '${podName}', :path => ansight_ios_path`)
    .join("\n");
  const sourceBlock = `  # BEGIN Ansight SDK source\n` +
    `  ansight_sdk_source = ENV.fetch('ANSIGHT_RN_HARNESS_PACKAGE_SOURCE', '${sourceMode}')\n` +
    `  if ansight_sdk_source == 'local'\n` +
    `    ansight_ios_path = File.expand_path('${iosPath}')\n` +
    `${localPods}\n` +
    `  elsif ansight_sdk_source == 'published'\n` +
    `    # AnsightReactNative.podspec resolves Ansight and AnsightObjC from CocoaPods trunk.\n` +
    `  else\n` +
    `    raise "Unsupported ANSIGHT_RN_HARNESS_PACKAGE_SOURCE: #{ansight_sdk_source}"\n` +
    `  end\n` +
    `  # END Ansight SDK source\n`;

  const markerPattern = /  # BEGIN Ansight SDK source\n[\s\S]*?  # END Ansight SDK source\n?/;
  const legacyPattern = /  ansight_ios_path = File\.expand_path\([^\n]+\)\n(?:  pod 'Ansight[^']*', :path => ansight_ios_path\n)+/;
  let podfile = read(podfilePath);
  if (markerPattern.test(podfile)) {
    podfile = podfile.replace(markerPattern, sourceBlock);
  } else if (legacyPattern.test(podfile)) {
    podfile = podfile.replace(legacyPattern, sourceBlock);
  } else {
    throw new Error(`Could not find Ansight pod block in ${podfilePath}`);
  }
  write(podfilePath, podfile);
}

const androidBuildGradlePath = path.join(harnessPath, "android/build.gradle");
if (fs.existsSync(androidBuildGradlePath)) {
  const repoBlock = `    // BEGIN Ansight SDK source repositories\n` +
    `    if ((System.getenv('ANSIGHT_RN_HARNESS_PACKAGE_SOURCE') ?: '${sourceMode}') == 'local') {\n` +
    `      mavenLocal()\n` +
    `    }\n` +
    `    // END Ansight SDK source repositories`;
  const markerPattern = /    \/\/ BEGIN Ansight SDK source repositories\n[\s\S]*?    \/\/ END Ansight SDK source repositories/;
  let gradle = read(androidBuildGradlePath);
  if (markerPattern.test(gradle)) {
    gradle = gradle.replace(markerPattern, repoBlock);
  } else if (gradle.includes("    mavenLocal()")) {
    gradle = gradle.replace("    mavenLocal()", repoBlock);
  } else {
    throw new Error(`Could not find mavenLocal() repository in ${androidBuildGradlePath}`);
  }
  write(androidBuildGradlePath, gradle);
}

console.log(`Configured React Native harness for ${sourceMode} Ansight packages (${version}).`);
NODE

if [[ "${run_install}" == "true" ]]; then
  (cd "${harness_path}" && npm install)
else
  echo "Run npm install in ${harness_path} before building if node_modules/package-lock need to change."
fi
