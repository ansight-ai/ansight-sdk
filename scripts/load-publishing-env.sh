#!/usr/bin/env bash

load_publishing_env() {
  local repo_root="$1"
  local env_file="${repo_root}/.env.publishing.local"

  if [[ ! -f "${env_file}" ]]; then
    return 0
  fi

  set -a
  # shellcheck disable=SC1090
  source "${env_file}"
  set +a
}
