#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_file="$(mktemp "${TMPDIR:-/tmp}/ansight-build-artifacts.XXXXXX.swift")"
trap 'rm -f "${output_file}"' EXIT

swift run \
  --package-path "${repo_root}/src/ios" \
  AnsightBuildTool \
  --output-file "${output_file}" \
  --target-directory "${repo_root}/src/ios/Sources/AnsightCore"

echo "Ansight Swift bundled-tool policy validated."
