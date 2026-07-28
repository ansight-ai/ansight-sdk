# Ansight Flutter feature harness

This app exercises the complete public Flutter SDK and both native bridges.
It covers runtime state, telemetry, screenshots, touch capture, Flutter widget
inspection, navigation, errors, pairing and sessions, properties, custom tools,
artifact providers, binary transfer, native options and capabilities, and
runtime logs.

Run it without automatic pairing:

```shell
flutter run
```

For a device-safe paired run, use the package launcher from `src/flutter/`.
It wraps Studio's signed public config in a pairing document containing the
current development host address. The signed config is not modified, and the
temporary compiler-defines file is deleted when `flutter run` exits:

```shell
dart run tool/run_harness.dart \
  --device <device-id> \
  --pairing-config "/path/to/ai.ansight.flutter.harness.ans.json" \
  --release
```

Pass `--host-address <lan-ip>` if the automatically selected interface is not
the one shared with the device. On first launch, accept the platform's local
network permission. The app id on Android and iOS is
`ai.ansight.flutter.harness`.

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
