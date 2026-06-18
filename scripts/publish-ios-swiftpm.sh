#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
skip_tests=false
create_tag=false
push_tag=false
tag_name="${ANSIGHT_RELEASE_TAG:-}"

usage() {
  cat <<'EOF'
Usage: scripts/publish-ios-swiftpm.sh [options]

SwiftPM publication is Git-tag based. Default mode validates the package.

Options:
  --tag              Create the release tag if it does not exist
  --push-tag         Push the release tag to origin
  --tag-name TAG     Override the default v<ios-podspec-version> tag
  --skip-tests       Skip swift test

Environment:
  ANSIGHT_RELEASE_TAG           Optional default tag name
  ANSIGHT_ALLOW_REMOTE_TOOLS    Defaults to true for package validation
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag)
      create_tag=true
      shift
      ;;
    --push-tag)
      create_tag=true
      push_tag=true
      shift
      ;;
    --tag-name)
      tag_name="${2:?missing tag name}"
      shift 2
      ;;
    --skip-tests)
      skip_tests=true
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

version="$(perl -ne 'print "$1\n" if /s\.version\s*=\s*"([^"]+)"/' "${repo_root}/src/ios/AnsightCore.podspec" | head -n 1)"

if [[ -z "${tag_name}" ]]; then
  tag_name="v${version}"
fi

if [[ "${skip_tests}" != "true" ]]; then
  (cd "${repo_root}/src/ios" && ANSIGHT_ALLOW_REMOTE_TOOLS="${ANSIGHT_ALLOW_REMOTE_TOOLS:-true}" swift test)
fi

if [[ "${create_tag}" == "true" ]]; then
  if ! git -C "${repo_root}" diff --quiet || ! git -C "${repo_root}" diff --cached --quiet; then
    echo "error: refusing to tag a dirty worktree" >&2
    echo "Commit the release metadata first, or set the tag manually after review." >&2
    exit 1
  fi

  if git -C "${repo_root}" rev-parse -q --verify "refs/tags/${tag_name}" >/dev/null; then
    echo "Tag ${tag_name} already exists locally."
  else
    git -C "${repo_root}" tag -a "${tag_name}" -m "Release ${tag_name}"
    echo "Created tag ${tag_name}."
  fi
fi

if [[ "${push_tag}" == "true" ]]; then
  git -C "${repo_root}" push origin "refs/tags/${tag_name}"
fi

echo "SwiftPM release tag: ${tag_name}"
