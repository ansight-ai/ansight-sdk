#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/scripts/load-publishing-env.sh"
load_publishing_env "${repo_root}"

publishing_type="${SONATYPE_CENTRAL_PUBLISHING_TYPE:-USER_MANAGED}"
deployment_name="${SONATYPE_CENTRAL_DEPLOYMENT_NAME:-}"
upload_url="${SONATYPE_CENTRAL_UPLOAD_URL:-https://central.sonatype.com/api/v1/publisher/upload}"
dry_run=false

usage() {
  cat <<'EOF'
Usage: scripts/upload-maven-central-bundle.sh [options] <maven-repository-dir-or-zip>

Uploads a Maven repository bundle to the Sonatype Central Publisher API.

Options:
  --publishing-type USER_MANAGED|AUTOMATIC
  --name NAME
  --dry-run

Environment:
  SONATYPE_CENTRAL_USERNAME    Central Portal user-token username
  SONATYPE_CENTRAL_PASSWORD    Central Portal user-token password
  SONATYPE_BEARER              Optional precomputed Bearer token payload
  SONATYPE_CENTRAL_UPLOAD_URL  Optional upload endpoint
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publishing-type)
      publishing_type="${2:?missing publishing type}"
      shift 2
      ;;
    --name)
      deployment_name="${2:?missing deployment name}"
      shift 2
      ;;
    --dry-run)
      dry_run=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --*)
      echo "error: unknown argument '$1'" >&2
      usage >&2
      exit 1
      ;;
    *)
      target="${1}"
      shift
      ;;
  esac
done

target="${target:-}"

if [[ -z "${target}" ]]; then
  usage >&2
  exit 1
fi

case "${publishing_type}" in
  USER_MANAGED|AUTOMATIC) ;;
  *)
    echo "error: --publishing-type must be USER_MANAGED or AUTOMATIC" >&2
    exit 1
    ;;
esac

if [[ -n "${deployment_name}" && ! "${deployment_name}" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "error: --name may only contain letters, numbers, dots, underscores, and dashes" >&2
  exit 1
fi

if [[ "${target}" != /* ]]; then
  target="${repo_root}/${target}"
fi

if [[ ! -e "${target}" ]]; then
  echo "error: '${target}' does not exist" >&2
  exit 1
fi

bundle_path="${target}"

if [[ -d "${target}" ]]; then
  if ! command -v jar >/dev/null 2>&1; then
    echo "error: jar is required to create the Central Portal bundle" >&2
    exit 1
  fi

  bundle_path="${target%/}.zip"
  rm -f "${bundle_path}"
  (cd "${target}" && jar cMf "${bundle_path}" .)
fi

query="publishingType=${publishing_type}"

if [[ -n "${deployment_name}" ]]; then
  query="${query}&name=${deployment_name}"
fi

echo "Maven Central bundle: ${bundle_path}"
echo "Upload URL: ${upload_url}?${query}"

if [[ "${dry_run}" == "true" ]]; then
  echo "Dry run only; upload skipped."
  exit 0
fi

token="${SONATYPE_BEARER:-${SONATYPE_CENTRAL_BEARER:-}}"
token="${token#Bearer }"

if [[ -z "${token}" ]]; then
  if [[ -z "${SONATYPE_CENTRAL_USERNAME:-}" || -z "${SONATYPE_CENTRAL_PASSWORD:-}" ]]; then
    echo "error: set SONATYPE_CENTRAL_USERNAME and SONATYPE_CENTRAL_PASSWORD, or SONATYPE_BEARER" >&2
    exit 1
  fi

  token="$(printf '%s:%s' "${SONATYPE_CENTRAL_USERNAME}" "${SONATYPE_CENTRAL_PASSWORD}" | base64 | tr -d '\n')"
fi

deployment_id="$(
  curl --fail --silent --show-error \
    --request POST \
    --header "Authorization: Bearer ${token}" \
    --form "bundle=@${bundle_path}" \
    "${upload_url}?${query}"
)"

deployment_file="${bundle_path}.deployment-id"
printf '%s\n' "${deployment_id}" > "${deployment_file}"

echo "Deployment ID: ${deployment_id}"
echo "Deployment ID written to ${deployment_file}"
