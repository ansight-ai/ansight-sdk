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

## Validation Evidence

Validated on 2026-06-15 with the Android modules on compile/target SDK 35:

- `src/android :ansight-runtime:compileDebugKotlin` passed.
- `src/android :ansight-runtime:testDebugUnitTest` passed.
- `src/android :harness:assembleDebug` passed.
- `src/android :ansight-runtime:publishReleasePublicationToMavenLocal` passed,
  publishing `ai.ansight:ansight-runtime-android:0.1.0-pre1`.
- Native Android corpus app `sogonov__anubis` was copied to
  `/tmp/ansight-android-validation-anubis`, patched to use `mavenLocal()`,
  depend on the local Ansight AAR, and call `AnsightRuntime.initializeAndActivate`
  from its existing `Application` subclass. `:app:assembleDebug` passed.

Runtime launch smoke was attempted with `Pixel_3a_API_33_arm64-v8a`, but the
local emulator did not register with ADB and emitted graphics library load
errors. A second attempt with `Pixel_9a` failed because that AVD has a broken
system image path. No device-backed Studio validation was completed in this
pass.

## Known Gaps

- Live Studio validation still needs a bootable emulator or attached Android
  device plus a fresh Android-targeted pairing config.
- Screenshot, touch streaming, visual tree, file/preferences/secure/database
  tools, and release-build tool policy belong to goals 07 through 13 and remain
  for the next Android passes.
