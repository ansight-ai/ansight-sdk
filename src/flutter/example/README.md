# Ansight Flutter feature harness

This app exercises the complete public Flutter SDK and both native bridges.
It covers runtime state, telemetry, screenshots, touch capture, Flutter widget
inspection, navigation, errors, pairing and sessions, properties, custom tools,
artifact providers, binary transfer, native options and capabilities, and
runtime logs.

Run it on a simulator or emulator while Studio is open and signed in:

```shell
flutter run
```

The SDK enrolls at runtime; the launcher does not request or inject a Studio
invite:

```shell
dart run tool/run_harness.dart --device <device-id>
```

For a physical phone, scan a Studio QR once from the harness's QR action and
accept the platform's local-network permission. The app id on Android and iOS
is `ai.ansight.flutter.harness`.

The app exposes pairing from the QR icon in the app bar and from
`Host pairing and sessions` → `QR pairing dialog`. The dialog can invoke each
platform's native camera scanner or accept a pasted pairing payload.

Screen capture deliberately has two independently testable paths:

- Built-in session capture: periodic development JPEG frames plus
  `harness.capture_builtin` and the in-app capture action.
- Host handoff capture: Studio's on-demand `ui.get_screenshot` tool, which
  captures through the simulator/device host integration.

Run its automated checks from the package root:

```shell
flutter test example/test
flutter test example/integration_test -d <device-id>
```
