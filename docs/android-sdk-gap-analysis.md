# Android SDK Gap Analysis

## First-Pass Scope

This pass implements SDK goals 00 through 06 for the native Android SDK under
`src/android`.

Implemented:

- Runtime integration: `AnsightRuntime.initialize`, `initializeAndActivate`,
  `activate`, `deactivate`, `clear`, manual metrics, events, screen views, and
  lifecycle state.
- Configuration: sampling frequency, retention, built-in memory/FPS/battery
  toggles, JPEG/touch option models, host auto-probe options, host connection
  options, custom properties, and reserved channel validation.
- Pairing and trust: `ansight.pairing-config.v1` and
  `ansight.pairing-config-document.v1` parsing, legacy ticket document schema
  recognition, expiry checks, expected app id checks, and ECDSA P-256 signature
  verification against the Studio host public key.
- Host connection: structured request/status/result models, saved config storage
  in app-private `SharedPreferences`, bundled config candidates, payload connect,
  disconnect, and reason-code mapping.
- Live transport: UDP `CONNECT_REQ` bootstrap, `CONNECT_RESP` parsing, OkHttp
  WebSocket handoff, correlated `CONTROL_REQ` acknowledgements, `session.open`,
  `device.profile`, `app.state`, client log, and `session.complete` actions.
- Device/app context: Android package, build, process, OS/API, emulator,
  locale/timezone, display, memory, storage, battery, network, runtime, SDK, and
  capability profile collection with PII-sensitive fields omitted.
- Telemetry capture: bounded sample buffers, built-in Java heap/native heap/RSS,
  FPS via `Choreographer`, optional battery percentage, custom channels, channel
  announcement, `CLIENT_METRICS`, and `CLIENT_EVENTS` streaming.
- Evidence and tooling: ASJP session JPEG streaming, `ui.get_screenshot`,
  Android visual tree and node inspection, diagnostic overlays, app sandbox file
  tools, SharedPreferences tools, secure-storage tools, SQLite schema/query
  tools, and binary file-transfer framing.
- Input capture: Android `Window.Callback` touch capture with packed
  `CLIENT_TOUCH_INPUT` streaming.

## Validation Evidence

Validated on 2026-06-15 with the Android modules on compile/target SDK 35:

- `src/android :ansight-runtime:compileDebugKotlin` passed.
- `src/android :ansight-runtime:testDebugUnitTest` passed.
- `src/android :harness:assembleDebug` passed.
- `src/android :ansight-runtime:publishReleasePublicationToMavenLocal` passed,
  publishing `ai.ansight:ansight-runtime-android:0.1.0-pre1`.
- Device-backed validation used attached Pixel 7 device `2A201FDH200BXX` and
  Ansight Studio `0.5.11`. The mounted MCP tools returned
  `MCP-Session-Id` initialization errors in this Codex thread, so validation used
  `/Applications/Ansight.app/Contents/Helpers/ansight-daemon mcp-stdio`.
- Studio pairing config `f265484fc8744df9b8b81c28b3a1af8b` opened live session
  `ai-ansight-harness-600` for `ai.ansight.harness` with status
  `WebSocket Open`.
- The live session reported 7 metric channels and 705 metric samples at the
  time of the artifact query. Memory and performance channels were present:
  Java heap, Native heap, RSS, FPS, and Battery Level.
- ASJP screenshot streaming reached Studio: the fresh session had 2 JPEG frames,
  and `ansight_take_screenshot` transferred a 720x1600 JPEG through the remote
  `ui.get_screenshot` tool.
- Touch capture reached Studio: `ansight_get_session_timeline` returned touch
  `down` and `up` events for an `adb input tap`, with normalized coordinates for
  the same live session.
- Remote tool discovery reached Studio: `ansight_list_app_tools` returned 28
  Android tools for the live harness session.
- Android corpus validation injected the local AAR into 25 copied Android test
  apps under `/tmp/ansight-android-corpus-validation`. All 25 apps passed
  `assembleDebug` with Ansight injected after temp-copy repairs for stale app
  build issues, including missing API keys, obsolete plugin repositories,
  missing submodule contents, a copied Gradle wrapper issue, and an invalid
  local debug keystore format.

## Known Gaps

- Android release-build tool policy still needs a dedicated consumer-facing
  workflow.
- Studio MCP mounted-tool access in this Codex thread still fails with an
  `MCP-Session-Id` initialization error; direct daemon stdio remains a working
  fallback.
