# Ansight Flutter feature harness

This app exercises the complete public Flutter SDK and both native bridges.
It covers runtime state, telemetry, screenshots, touch capture, Flutter widget
inspection, navigation, errors, pairing and sessions, properties, custom tools,
artifact providers, binary transfer, native options and capabilities, and
runtime logs.

Run it on a simulator, emulator, or macOS desktop while the CLI host is
running:

```shell
flutter run
```

The SDK enrolls at runtime; the launcher does not request or inject a host
invite:

```shell
dart ../../../src/flutter/tool/run_harness.dart --device <device-id>
```

For a physical phone, scan a host QR once from the harness's QR action and
accept the platform's local-network permission. The app id on Android, iOS,
and macOS is `ai.ansight.flutter.harness`.

The app exposes pairing from the QR icon in the app bar and from
`Host pairing and sessions` → `QR pairing dialog`. The dialog can invoke each
platform's native camera scanner or accept a pasted pairing payload.

On Android or iOS, use Runtime → `Annotate` to open the native editor. Save a
marked screen and check the returned status and annotation id in the activity
log, then inspect the connected session. Repeat while disconnected to check
queued delivery after reconnecting. The editor is unavailable on macOS.

Screen capture deliberately has two independently testable paths:

- Built-in session capture: periodic development JPEG frames plus
  `harness.capture_builtin` and the in-app capture action.
- Host handoff capture: host's on-demand `ui.get_screenshot` tool, which
  captures through the simulator/device host integration.

Run its automated checks from the example directory:

```shell
flutter test test
flutter test integration_test -d <device-id>
```

The persistent macOS runner and retained-session workflow live in the
[`flutter/macos`](../../../../../ansight-test-apps/flutter/macos/README.md) project. Its `scripts/record.sh`
command runs this harness, resolves the exact new session, pins and tags the
recording, and writes `validation/latest-session.json`.
