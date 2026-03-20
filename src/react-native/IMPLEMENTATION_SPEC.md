# Ansight React Native Bridge Implementation Spec

This document defines the React Native Ansight package as a bridge over the native Android and iOS Ansight runtimes.

It is not a protocol implementation. The protocol implementation belongs in:

- `src/android`
- `src/ios`

`.NET` remains the current protocol reference implementation, but is out of scope for this platform plan.

## Role in the architecture

React Native should provide:

- the JS-facing API
- ergonomic configuration and session calls
- custom metric and event helpers
- optional React Navigation or app lifecycle integrations
- optional lightweight JS-defined tools

React Native should not own:

- pairing document validation
- UDP discovery/connect flow
- WebSocket transport
- ack queue logic
- screenshot capture and `ASJP` framing
- built-in telemetry samplers
- built-in privileged tools

Those belong in the native Android and iOS runtimes.

## Dependencies

The React Native package should depend on:

- the Android runtime in `src/android`
- the iOS runtime in `src/ios`

That dependency should be real, not conceptual. The React Native package should fail to build without those runtimes present.

## Package shape

Recommended shape:

- npm package under `src/react-native`
- TurboModule or equivalent modern native bridge
- Android native module that delegates to `src/android`
- iOS native module that delegates to `src/ios`

Recommended layout:

- `src/react-native/package.json`
- `src/react-native/src/index.ts`
- `src/react-native/src/runtime.ts`
- `src/react-native/src/pairing.ts`
- `src/react-native/src/tools.ts`
- `src/react-native/src/types.ts`
- `src/react-native/android/...`
- `src/react-native/ios/...`

## Native dependency contract

The bridge should treat the native runtimes as the source of truth for:

- protocol behavior
- options validation
- device profile collection
- session lifecycle
- telemetry retention and streaming
- tool guard rules

The JS layer should only pass:

- app configuration
- custom metrics and events
- optional profile augmentation metadata
- optional JS tool registrations

## Public API

Recommended JS-facing API:

```ts
export type AnsightOptions = {
  sampleFrequencyMilliseconds?: number;
  retentionPeriodSeconds?: number;
  enableFramesPerSecond?: boolean;
  additionalChannels?: AnsightChannel[];
  sessionJpegCapture?: SessionJpegCaptureOptions | null;
  toolGuard?: ToolGuard;
};

export type PairingOpenOptions = {
  clientName: string;
  manualHostAddress: string;
  expectedAppId?: string;
  profileOverride?: Partial<DeviceAppProfile>;
};

export const Ansight: {
  initialize(options?: AnsightOptions): Promise<void>;
  activate(): Promise<void>;
  deactivate(): Promise<void>;
  clear(): Promise<void>;
  metric(value: number | string, channel?: number): Promise<void>;
  event(
    label: string,
    options?: {
      type?: AnsightEventType;
      details?: string;
      channel?: number;
      id?: string;
    }
  ): Promise<void>;
  openSession(pairingJson: string, options: PairingOpenOptions): Promise<OpenSessionResult>;
  completeSession(): Promise<void>;
  closeSession(): Promise<void>;
  registerTool(tool: AnsightTool): Promise<void>;
};
```

Notes:

- `number | string` for metric values preserves 64-bit values safely through JS
- the bridge should expose async APIs even when native work is fast
- direct access to protocol internals should not be part of the JS contract

## Bridge responsibilities

### JS responsibilities

The JS layer should own:

- typed models
- developer ergonomics
- custom metrics and custom events
- app-specific annotations
- optional navigation instrumentation
- optional registration of short-running JS-defined tools

### Native bridge responsibilities

The platform bridge code should:

- translate JS models into native runtime calls
- subscribe to native events when needed
- pass framework metadata into native profile augmentation
- never duplicate protocol state machines

## Framework metadata to pass to native runtimes

The React Native bridge should provide native runtimes with:

- framework tag such as `react-native`
- runtime stack augmentation:
  - React layer with `runtimeCode = 1`
  - JS engine metadata such as Hermes or JSC as `other`
- app tags or release channel tags if configured

The native runtime should merge this into its baseline `DeviceAppProfile`.

## Tool model

Built-in tools should remain native.

JS-defined tools should be optional and constrained:

- discovery and execution remain subject to native tool guard policy
- JS tools should be read-only by default
- JS tools should be short-running
- JS tools should receive arguments already flattened to strings

Recommended v1 approach:

- ship without JS-defined tools initially
- support native tools first
- add JS tools only after bridge stability is proven

## What React Native cannot honestly own

These should not be implemented in JS:

- UDP transport
- exact pairing signature verification
- screenshot capture
- `ASJP` frame encoding
- request/ack ordering
- tool timeout enforcement
- file system and database privileged tools

If the bridge tries to own these, it becomes the second protocol implementation and will drift from Android and iOS quickly.

## Capability gaps and non-parity areas

### JS heap metrics are not equivalent to `.NET`

There is no straightforward production-safe equivalent to `.NET` managed heap telemetry in React Native.

Recommendation:

- focus on:
  - custom metrics
  - native platform memory metrics
  - native frame timing
- treat JS heap metrics as optional/debug-only until proven safe and stable

### UI inspection is framework-specific

React Native view inspection should not be promised as parity with native Android, native iOS, or `.NET` visual tree tools.

Recommendation:

- do not promise `ui.get_visual_tree` parity in v1
- if JS/UI inspection is added later, make it explicitly React Native-specific

### JS tools are less reliable than native tools

Tool responses in Studio currently time out after `20s`. JS thread stalls make long-running JS tools a weak default.

Recommendation:

- native tools first
- JS tools later and only for low-latency operations

## Delivery plan

### Phase 0

- wait for Android and iOS runtime specs to be accepted
- define native runtime bridge surface

### Phase 1

- create the React Native package scaffold
- connect to native runtimes
- expose initialize/activate/deactivate/metric/event

### Phase 2

- expose session open/close/complete APIs
- pass profile augmentation metadata into native runtimes

### Phase 3

- add native event subscriptions if needed
- add navigation/app lifecycle helpers

### Phase 4

- integrate native tools
- optionally add JS tool registration

## Testing plan

Required coverage:

- JS API contract tests
- Android bridge integration tests against the Android runtime
- iOS bridge integration tests against the iOS runtime
- live Studio interop tests through the native runtimes

The React Native package should not have its own protocol fixture suite beyond bridge contract coverage.
