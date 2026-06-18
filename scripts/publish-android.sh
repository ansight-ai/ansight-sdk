#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="local"
skip_tests=false
gradle_args=()

usage() {
  cat <<'EOF'
Usage: scripts/publish-android.sh [options]

Default mode validates and publishes Android AARs to Maven local.

Options:
  --publish              Publish to ANSIGHT_MAVEN_URL with Gradle maven-publish
  --central-bundle       Publish signed artifacts to build/android-maven-central and zip them
  --publish-central      Build a Central Portal bundle and upload it
  --skip-tests           Skip Android unit/harness validation
  -Pkey=value            Forward a Gradle property

Environment:
  ANSIGHT_MAVEN_URL              Maven repository URL for --publish
  ANSIGHT_MAVEN_USERNAME         Optional repository username
  ANSIGHT_MAVEN_PASSWORD         Optional repository password
  ANSIGHT_GPG_SIGNING_KEY        ASCII-armored signing key for Central
  ANSIGHT_GPG_SIGNING_PASSWORD   Signing key password
  SONATYPE_CENTRAL_USERNAME      Central Portal token username for --publish-central
  SONATYPE_CENTRAL_PASSWORD      Central Portal token password for --publish-central
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish)
      mode="remote"
      shift
      ;;
    --central-bundle)
      mode="central-bundle"
      shift
      ;;
    --publish-central)
      mode="publish-central"
      shift
      ;;
    --skip-tests)
      skip_tests=true
      shift
      ;;
    -P*)
      gradle_args+=("$1")
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

if [[ "${mode}" == "remote" && -z "${ANSIGHT_MAVEN_URL:-}" ]]; then
  echo "error: set ANSIGHT_MAVEN_URL or use --central-bundle/--publish-central" >&2
  exit 1
fi

if [[ "${mode}" == "publish-central" || "${mode}" == "central-bundle" ]]; then
  if [[ -z "${ANSIGHT_GPG_SIGNING_KEY:-}" || -z "${ANSIGHT_GPG_SIGNING_PASSWORD:-}" ]]; then
    echo "error: set ANSIGHT_GPG_SIGNING_KEY and ANSIGHT_GPG_SIGNING_PASSWORD for Central artifacts" >&2
    exit 1
  fi
fi

cd "${repo_root}/src/android"

if [[ "${skip_tests}" != "true" ]]; then
  ./gradlew :ansight-core:test :ansight:test :harness:assembleDebug "${gradle_args[@]+"${gradle_args[@]}"}"
fi

case "${mode}" in
  local)
    ./gradlew publishReleasePublicationToMavenLocal "${gradle_args[@]+"${gradle_args[@]}"}"
    ;;
  remote)
    ./gradlew publish "${gradle_args[@]+"${gradle_args[@]}"}"
    ;;
  central-bundle|publish-central)
    central_repo="${repo_root}/build/android-maven-central"
    rm -rf "${central_repo}"
    mkdir -p "${central_repo}"

    ./gradlew publish \
      -PansightMavenUrl="file://${central_repo}" \
      -PansightMavenRepositoryName="ansightCentral" \
      "${gradle_args[@]+"${gradle_args[@]}"}"

    version="$(sed -n 's/^ansightAndroidVersion=//p' "${repo_root}/src/android/gradle.properties" | head -n 1)"
    deployment_name="${SONATYPE_CENTRAL_DEPLOYMENT_NAME:-ansight-android-${version}}"

    if [[ "${mode}" == "publish-central" ]]; then
      "${repo_root}/scripts/upload-maven-central-bundle.sh" \
        --name "${deployment_name}" \
        "${central_repo}"
    else
      "${repo_root}/scripts/upload-maven-central-bundle.sh" \
        --dry-run \
        --name "${deployment_name}" \
        "${central_repo}"
    fi
    ;;
esac
