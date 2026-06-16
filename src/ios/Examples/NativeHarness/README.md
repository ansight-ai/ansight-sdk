# Ansight iOS Native Harness

This app is the native iOS validation harness for the Swift SDK. It is intentionally small, but it exercises the aggregate `Ansight` SwiftPM product, pairing flows, telemetry, screen capture, touch capture, FPS capture, and all current remote tool suites.

The harness bundle id is:

```text
ai.ansight.ios.native-harness
```

## Generate and Run

From this directory:

```sh
xcodegen generate
open AnsightNativeHarness.xcodeproj
```

Run the `AnsightNativeHarness` scheme on an iOS simulator or device.

## Pairing

The app initializes the SDK on launch and registers the aggregate remote tools through:

```swift
try AnsightRuntime.shared.initializeAndActivateAnsightSdk(...)
```

Use one of the pairing buttons in the app:

- `Auto Connect` uses saved, bundled, or cached pairing state.
- `Pairing File` opens the native document picker for an Ansight Studio pairing config.
- `Scan QR` opens the native QR pairing scanner.

The harness bundles `src/ios/ansight.json` as an app resource and passes it to `AnsightHostConnectionOptions.bundledConfigJson`. Studio-issued public configs are enough to validate bundling, but live pairing still needs host discovery from a QR/file/config-document payload. If the bundled config includes host discovery, the app tries pairing on launch; otherwise use `Scan QR` or `Pairing File`.

## Validation Checklist

After pairing with Ansight Studio, validate:

- App profile: app id, app name, icon, simulator/device details.
- Telemetry: custom metric channel `42`, lifecycle events, manual events, screen views, FPS metrics.
- Screen capture: live JPEG stream and the manual `Capture Frame` button.
- Touch capture: tap, drag, keyboard focus, picker focus, and toggle interactions.
- Visual tree: SwiftUI content plus UIKit `UITextField` picker input view.
- Modal picker overlay capture: tap `Shipping Speed` and inspect the captured screen frame while the native picker is open.
- Preferences tools: keys under `ansight.harness.`.
- File-system tools: `documents/ansight-harness/hello.txt`.
- Database tools: `documents/ansight-harness/sample.sqlite`, table `harness_events`.
- Secure-storage tools: service `ai.ansight.ios.native-harness.secure`, key `ansight.harness.token`.

The `Re-seed Harness Data` button rewrites the sample Preferences, Documents, SQLite, and Keychain data without reinstalling the app.
