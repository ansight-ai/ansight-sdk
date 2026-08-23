## 1.3.0-preview.11

- Add opt-in HTTP request capture with typed request and response models.
- Include bounded request and response bodies by default, with dynamic controls
  for excluding either body and support for larger configured limits.
- Redact credential headers, sensitive URL parameters, cloud-signed URLs, and
  captured body content before forwarding requests to a connected host.

## 1.3.0-preview.10

- Capture touch-triggered visual trees only on touch down and touch up across
  all native runtimes; move and cancel events no longer trigger capture.
- Include every top-level Android window root in managed visual-tree snapshots.
- Align Flutter's native dependencies with the synchronized SDK release.

## 1.3.0-preview.9

- Align native dependencies with the synchronized SDK hotfix that prevents
  duplicate Okio bytecode in .NET Android applications.

## 1.3.0-preview.8

- Add opt-in keyboard-presence metadata to session JPEG capture on Android and
  iOS.
- Align the Flutter package's native dependencies with the synchronized
  1.3.0-preview.8 SDK release.

## 1.3.0-preview.5

- Align the Flutter package's Android and iOS runtime dependencies with the
  SDK release that supports opt-in unattended physical-device provisioning.

## 1.3.0-preview.3

- Use the compact v2 visual-tree contract with a shared type registry,
  nested nodes, and optional `z` ordering metadata.
- Remove legacy per-node type, kind, style, and stacking fields.

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
