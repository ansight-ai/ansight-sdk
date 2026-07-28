#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/scripts/load-publishing-env.sh"
load_publishing_env "${repo_root}"

publish=false
skip_install=false
skip_check=false

usage() {
  cat <<'EOF'
Usage: scripts/publish-capacitor.sh [options]

Default mode installs package dev dependencies if needed, verifies the package,
and executes npm pack --dry-run. Use --publish to publish to npm.

Options:
  --publish       Run npm publish --access public
  --skip-install  Do not install missing node_modules
  --skip-check    Skip npm run verify

Environment:
  NPM_TOKEN or npm login must be configured for --publish.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish)
      publish=true
      shift
      ;;
    --skip-install)
      skip_install=true
      shift
      ;;
    --skip-check)
      skip_check=true
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

if ! command -v npm >/dev/null 2>&1; then
  echo "error: npm is required" >&2
  exit 1
fi

if [[ -n "${NPM_TOKEN:-}" ]]; then
  npm_config_userconfig="$(mktemp)"
  export npm_config_userconfig
  printf '//registry.npmjs.org/:_authToken=%s\n' "${NPM_TOKEN}" > "${npm_config_userconfig}"
  trap 'rm -f "${npm_config_userconfig}"' EXIT
fi

cd "${repo_root}/src/capacitor"

if [[ "${skip_install}" != "true" && ! -d node_modules ]]; then
  npm ci
fi

if [[ "${skip_check}" != "true" ]]; then
  npm run verify
fi

npm pack --dry-run

if [[ "${publish}" == "true" ]]; then
  package_version="$(node -p 'require("./package.json").version')"
  npm_tag="${NPM_TAG:-latest}"
  if [[ "${package_version}" == *-* && -z "${NPM_TAG:-}" ]]; then
    npm_tag="preview"
  fi

  npm publish --access public --tag "${npm_tag}"
fi
