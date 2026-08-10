## 1.3.0-preview.2

- Add semantic roles, interactability, supported actions, and stable automation
  identifiers to cross-platform visual-tree nodes.
- Report effective runtime availability and unmet preconditions for advertised
  tools.

## 1.3.0-preview.1

- Add screenshot-and-visual-tree session capture with Studio recording
  correlation.
- Include string widget keys as automation identifiers in Flutter visual-tree
  snapshots.
- Align the native Android and iOS runtime dependencies with the 1.3 preview
  SDK family.

## 1.2.0-preview.2

- Align Flutter package metadata and native dependencies with the
  1.2.0-preview.2 SDK family.

## 1.2.0-preview.1

- Add platform-neutral foreground, background, opacity, displayed text, and
  value data to visual-tree snapshots.
- Bound captured presentation strings and omit secure text-field values.

## 1.1.0-preview.1

- Add zero-touch local Studio enrollment for developer builds.
- Add generic one-use physical-device enrollment QR support across Android,
  iOS, .NET, React Native, Capacitor, and Flutter.
- Bind generic enrollment grants to the scanning app and installation while
  preserving automatic reconnects.

## 1.0.2-preview.8

- Add an explicit, default-off cellular host-connection policy across native
  SDKs and Flutter.
- Allow trusted development builds to opt in with
  `withCellularHostConnections()`.
- Apply the policy consistently to bundled configs, QR scans, saved profiles,
  automatic reconnects, and manual connection attempts.

## 1.0.2-preview.7

- Republish the preview 6 feature set with corrected CocoaPods source metadata.

## 1.0.2-preview.6

- Add the first complete Ansight Flutter plugin for Android and iOS.
- Add runtime lifecycle, telemetry, host pairing, session, property, capture,
  connection-status, and native capability APIs.
- Add Flutter lifecycle, route, frame-timing, error, and widget-tree
  instrumentation.
- Add custom Dart tools, artifact providers, and chunked binary transfer.
- Add a full interactive feature harness and native integration tests.
- Support both SDK-managed and simulator/host-handoff screen capture.
- Add accessible QR pairing and physical-device pairing configuration helpers.
- Support Flutter 3.0+, Android API 24+, and iOS 15+.
