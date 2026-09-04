# Ansight iOS Native Harness

This app is the native iOS validation harness for the Swift SDK. It exercises the aggregate `Ansight` SwiftPM product, pairing flows, telemetry, screen capture, touch capture, FPS capture, navigation surfaces, inline 3D content, seeded storage, and all current remote tool suites.

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

## Package Source

The default project uses the local SwiftPM package at `src/ios`:

```sh
xcodegen generate --spec project.yml
open AnsightNativeHarness.xcodeproj
```

To validate the published SwiftPM package for the current SDK version:

```sh
xcodegen generate --spec project.published.yml
open AnsightNativeHarnessPublished.xcodeproj
```

Published SwiftPM validation requires tags created after the repository root
`Package.swift` was added. Older tags that only contain `src/ios/Package.swift`
cannot be resolved by SwiftPM from the Git repository URL.

## Enrollment

The app initializes the SDK on launch and registers the aggregate remote tools through:

```swift
try AnsightRuntime.shared.initializeAndActivateAnsightSdk(...)
```

Use one of the enrollment buttons in the app:

- `Auto Connect` reconnects from app-private enrollment state.
- `Scan Enrollment QR` opens the native scanner for first-use registration.

The first successful scan stores a random installation id and enrollment state
privately. Host auto-probe retries that remembered registration while the
runtime is active, so the app reconnects after host disappears and later
reappears. The harness has no bundled connection file or build-time secret.

## Validation Checklist

After enrollment with Ansight host, validate:

- App profile: app id, app name, icon, simulator/device details.
- Telemetry: custom metric channel `42`, lifecycle events, manual events, screen views, FPS metrics.
- Screen capture: live JPEG stream and the manual `Capture Frame` button.
- Touch capture: tap, drag, keyboard focus, picker focus, and toggle interactions.
- Navigation: tabs, push/pop stack, sheet modal, full-screen modal, menu flyout, and custom drawer flyout.
- Inline 3D: SceneKit viewer renders cube/sphere/torus content, supports material changes, rotation controls, camera interaction, and node tap selection.
- Visual tree: SwiftUI content plus UIKit `UITextField` picker input view.
- Modal picker overlay capture: tap `Shipping Speed` and inspect the captured screen frame while the native picker is open.
- Preferences tools: keys under `ansight.harness.`.
- File-system tools: `documents/ansight-harness/hello.txt`.
- Database tools: `documents/ansight-harness/sample.sqlite`, tables `harness_events`, `harness_orders`, `harness_inventory`, and `harness_navigation_events`.
- Secure-storage tools: service `ai.ansight.ios.native-harness.secure`, key `ansight.harness.token`.
- Custom tools: `harness.state.snapshot`, `harness.reflection_roots.list`, and `harness.reflection_root.inspect`.

> **Important:** Screen capture will result in an FPS drop while frames are
> rendered, encoded, and sent. Use this harness to validate capture fidelity, not
> to measure baseline rendering performance with capture enabled.

The `Re-seed Harness Data` button rewrites the sample Preferences, Documents, SQLite, and Keychain data without reinstalling the app.

## Harness Reflection Roots

The harness registers custom inspection roots for manual host validation:

- `ui.orderDraft`: bound text, picker, toggle, and quantity state.
- `navigation.flow`: selected tab, active modal, flyout selection, pushed depth, and recent navigation events.
- `scene.inline3d`: SceneKit material, rotation state, spin speed, and selected node.
- `data.seededStore`: seeded file/database/preferences/keychain metadata.
- `runtime.snapshot`: current Ansight runtime counters and capture status.

`harness.reflection_roots.list` includes `hostRuntime` on every root. The
native iOS harness reports `kind: "swift"` for roots owned by the Swift and
Objective-C runtime.
