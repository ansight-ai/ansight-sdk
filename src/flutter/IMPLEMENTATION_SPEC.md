# Ansight Flutter Bridge Implementation Spec

This document defines the Flutter Ansight package as a bridge over the native Android and iOS Ansight runtimes.

It is not a protocol implementation. The protocol implementation belongs in:

- `src/android`
- `src/ios`

`.NET` remains the current protocol reference implementation, but is out of scope for this platform plan.

## Role in the architecture

Flutter should provide:

- the Dart-facing API
- ergonomic configuration and session calls
- custom metric and event helpers
- optional Navigator or app lifecycle integrations
- optional lightweight Dart-defined tools

Flutter should not own:

- pairing document validation
- UDP discovery/connect flow
- WebSocket transport
- ack queue logic
- screenshot capture and `ASJP` framing
- built-in telemetry samplers
- built-in privileged tools

Those belong in the native Android and iOS runtimes.

## Dependencies

The Flutter plugin should depend on:

- the Android runtime in `src/android`
- the iOS runtime in `src/ios`

That dependency should be real, not conceptual. The Flutter plugin should fail to build without those runtimes present.

## Package shape

Recommended shape:

- federated Flutter plugin under `src/flutter`
- Android implementation delegates to `src/android`
- iOS implementation delegates to `src/ios`

Recommended layout:

- `src/flutter/pubspec.yaml`
- `src/flutter/lib/ansight_flutter.dart`
- `src/flutter/lib/src/runtime.dart`
- `src/flutter/lib/src/pairing.dart`
- `src/flutter/lib/src/tools.dart`
- `src/flutter/lib/src/types.dart`
- `src/flutter/android/...`
- `src/flutter/ios/...`
- `src/flutter/example/...`

## Native dependency contract

The Flutter bridge should treat the native runtimes as the source of truth for:

- protocol behavior
- options validation
- device profile collection
- session lifecycle
- telemetry retention and streaming
- tool guard rules

The Dart layer should only pass:

- app configuration
- custom metrics and events
- optional profile augmentation metadata
- optional Dart tool registrations

## Public API

Recommended Dart-facing API:

```dart
class Ansight {
  static Future<void> initialize([AnsightOptions options = const AnsightOptions()]);
  static Future<void> activate();
  static Future<void> deactivate();
  static Future<void> clear();

  static Future<void> metric(int value, {int channel = AnsightChannels.unspecified});
  static Future<void> event(
    String label, {
    AnsightEventType type = AnsightEventType.info,
    String? details,
    int channel = AnsightChannels.unspecified,
    String? id,
  });

  static Future<OpenSessionResult> openSession(String pairingJson, PairingOpenOptions options);
  static Future<void> completeSession();
  static Future<void> closeSession();

  static Future<void> registerTool(AnsightTool tool);
}
```

Notes:

- Dart `int` is the right public type for metric values
- the bridge should expose async APIs consistently
- direct access to protocol internals should not be part of the Dart contract

## Bridge responsibilities

### Dart responsibilities

The Dart layer should own:

- typed models
- developer ergonomics
- custom metrics and custom events
- app-specific annotations
- optional navigation instrumentation
- optional registration of short-running Dart-defined tools

### Native bridge responsibilities

The platform bridge code should:

- translate Dart models into native runtime calls
- subscribe to native events when needed
- pass framework metadata into native profile augmentation
- never duplicate protocol state machines

## Framework metadata to pass to native runtimes

The Flutter bridge should provide native runtimes with:

- framework tag such as `flutter`
- runtime stack augmentation:
  - Flutter layer with `runtimeCode = 4`
  - Dart runtime as `other`
- render backend hints when known:
  - Skia
  - Impeller
- app tags or release channel tags if configured

The native runtime should merge this into its baseline `DeviceAppProfile`.

## Tool model

Built-in tools should remain native.

Dart-defined tools should be optional and constrained:

- discovery and execution remain subject to native tool guard policy
- Dart tools should be read-only by default
- Dart tools should be short-running
- Dart tools should receive arguments already flattened to strings

Recommended v1 approach:

- ship without Dart-defined tools initially
- support native tools first
- add Dart tools only after bridge stability is proven

## What Flutter cannot honestly own

These should not be implemented in Dart:

- UDP transport
- exact pairing signature verification
- screenshot capture
- `ASJP` frame encoding
- request/ack ordering
- tool timeout enforcement
- file system and database privileged tools

If the bridge tries to own these, it becomes the second protocol implementation and will drift from Android and iOS quickly.

## Capability gaps and non-parity areas

### Dart heap metrics are not equivalent to `.NET`

There is no straightforward production-safe equivalent to `.NET` managed heap telemetry in Flutter.

Recommendation:

- focus on:
  - custom metrics
  - native platform memory metrics
  - native frame timing
- treat Dart heap metrics as optional/debug-only until proven safe and stable

### UI inspection is framework-specific

Flutter widget inspection should not be promised as parity with native Android, native iOS, or `.NET` visual tree tools.

Recommendation:

- do not promise `ui.get_visual_tree` parity in v1
- if widget inspection is added later, make it explicitly Flutter-specific

### Dart tools are less reliable than native tools

Tool responses in Studio currently time out after `20s`. Isolate stalls or app lifecycle edge cases make long-running Dart tools a weak default.

Recommendation:

- native tools first
- Dart tools later and only for low-latency operations

## Delivery plan

### Phase 0

- wait for Android and iOS runtime specs to be accepted
- define native runtime bridge surface

### Phase 1

- create the Flutter plugin scaffold
- connect to native runtimes
- expose initialize/activate/deactivate/metric/event

### Phase 2

- expose session open/close/complete APIs
- pass profile augmentation metadata into native runtimes

### Phase 3

- add native event subscriptions if needed
- add Navigator/app lifecycle helpers

### Phase 4

- integrate native tools
- optionally add Dart tool registration

## Testing plan

Required coverage:

- Dart API contract tests
- Android bridge integration tests against the Android runtime
- iOS bridge integration tests against the iOS runtime
- live Studio interop tests through the native runtimes

The Flutter package should not have its own protocol fixture suite beyond bridge contract coverage.
