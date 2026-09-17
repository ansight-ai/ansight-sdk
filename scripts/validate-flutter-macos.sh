#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
example_root="${repo_root}/src/flutter/example"
app_id="ai.ansight.flutter.harness"
started_utc="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

if ! command -v flutter >/dev/null 2>&1; then
  echo "error: flutter is required" >&2
  exit 1
fi

if ! command -v ansight >/dev/null 2>&1; then
  echo "error: ansight CLI is required" >&2
  exit 1
fi

if ! ansight host status --json | jq -e '.isRunning == true' >/dev/null; then
  echo "error: start the resident host with 'ansight host run'" >&2
  exit 1
fi

(
  cd "${example_root}"
  export ANSIGHT_USE_LOCAL_SDK=1
  export ANSIGHT_LOCAL_SDK_PATH="${repo_root}/src/ios"
  flutter pub get
  flutter build macos --debug
  flutter test integration_test/plugin_integration_test.dart -d macos
)

session_id=""
for _ in $(seq 1 20); do
  listing="$(
    ansight session list \
      --app-id "${app_id}" \
      --platform macos \
      --from "${started_utc}" \
      --has-telemetry \
      --limit 20 \
      --json
  )"
  session_id="$(
    jq -r '.sessions | sort_by(.createdUtc) | last | .sessionId // empty' \
      <<<"${listing}"
  )"
  if [[ -n "${session_id}" ]]; then
    break
  fi
  sleep 1
done

if [[ -z "${session_id}" ]]; then
  echo "error: no recorded Flutter macOS session was found" >&2
  exit 1
fi

evidence_dir="$(mktemp -d)"
trap 'rm -rf "${evidence_dir}"' EXIT
session_file="${evidence_dir}/session.json"
fps_file="${evidence_dir}/fps.json"
images_file="${evidence_dir}/images.json"
trees_file="${evidence_dir}/trees.json"
touches_file="${evidence_dir}/touches.json"
ansight session show "${session_id}" --json >"${session_file}"
ansight session metrics "${session_id}" --channel 3 --limit 100 --json >"${fps_file}"
ansight session images "${session_id}" --limit 100 --json >"${images_file}"
ansight session trees "${session_id}" --limit 100 --json >"${trees_file}"
ansight session touches "${session_id}" --limit 100 --json >"${touches_file}"

jq -n -e \
  --slurpfile session "${session_file}" \
  --slurpfile fps "${fps_file}" \
  --slurpfile images "${images_file}" \
  --slurpfile trees "${trees_file}" \
  --slurpfile touches "${touches_file}" '
    $session[0].session.appId == "ai.ansight.flutter.harness" and
    $session[0].session.deviceProfile.device.osName == "macos" and
    $session[0].session.totalMetricSampleCount > 0 and
    ($fps[0].samples | length) > 0 and
    $images[0].totalCount > 0 and
    $trees[0].totalCount > 0 and
    $touches[0].totalCount > 0 and
    ($trees[0].items | any(
      . as $tree |
      $tree.visualTreeKind == "flutter" and
      $tree.nodeCount > 0 and
      $tree.truncated == false and
      $tree.screenshotCapturedAtUtc == $tree.capturedAtUtc and
      ([
        $tree.payload.root | .. | objects | .automationId? // empty
      ] | index("harness-scroll")) != null and
      ([
        $tree.payload.root | .. | objects | .automationId? // empty
      ] | index("run-e2e-scenario")) != null and
      ($images[0].items | any(.capturedAtUtc == $tree.screenshotCapturedAtUtc))
    ))
  ' >/dev/null

jq -n \
  --arg sessionId "${session_id}" \
  --slurpfile fps "${fps_file}" \
  --slurpfile images "${images_file}" \
  --slurpfile trees "${trees_file}" \
  --slurpfile touches "${touches_file}" '{
    sessionId: $sessionId,
    screenshots: $images[0].totalCount,
    visualTrees: $trees[0].totalCount,
    touches: $touches[0].totalCount,
    fpsSamples: ($fps[0].samples | length),
    latestFps: ($fps[0].samples | last | .value)
  }'
