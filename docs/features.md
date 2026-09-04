# Ansight SDK Feature Catalog

This catalog lists the application-facing features implemented in the current
SDK source. Use it to choose a package or platform guide; use
[Cross-SDK API Parity](sdk-api-parity.md) for exact API names and
[Protocol](protocol.md) for wire contracts.

Legend:

- **Yes**: the platform has a first-class implementation.
- **Native**: the JavaScript SDK exposes the feature through its Android/iOS
  bridge.
- **Framework**: the feature belongs to the named framework integration.
- **No**: the feature is not implemented for that SDK.

## Runtime and connection

| Capability | .NET / MAUI | Android | iOS | React Native | Capacitor |
| --- | --- | --- | --- | --- | --- |
| Initialize, activate, deactivate, and clear the runtime | Yes | Yes | Yes | Native | Native |
| Custom metric channels and metric/event recording | Yes | Yes | Yes | Native | Native |
| Periodically sampled app metric streams | Record with `Runtime.Metric(...)` | `AnsightMetricStream` | `AnsightMetricStream` | No JavaScript sampler API | No JavaScript sampler API |
| FPS, memory, and battery sampling | Yes | Yes | Yes | Native, plus React Native JS heap channels | Native |
| Screen-view and app-lifecycle events | Yes; automatic in `Ansight.Maui` | Yes | Yes; automatic UIKit capture | Native plus AppState and React Navigation helpers | Native plus WebView lifecycle and route helpers |
| Retained metric/event readback and debug snapshots | Yes | Yes | Yes | Native | Native |
| Device/app profile reporting | Yes | Yes | Yes | Native | Native |
| Live telemetry, event, touch, screenshot, and control streaming | Yes | Yes | Yes | Native | Native |
| App-provided live-session logs | Yes | Yes | Yes | Native | Native |
| Grouped session/custom properties and live mutations | Yes | Yes | Yes | Native | Native |
| App-private enrollment registration | Yes | Yes | Yes | Native | Native |
| Zero-touch host-local enrollment | Yes | Yes | Yes | Native | Native |
| Remembered host profiles and host auto-probe | Yes | Yes | Yes | Native | Native |
| Explicit payload connection | Yes | Yes | Yes | Yes | Yes |
| SDK-owned enrollment UI | Android/iOS QR via `Ansight.Pairing` | QR scanner | UIKit QR scanner | Native QR bridge | Native QR bridge |
| Connection status, capabilities, and change listeners | Yes | Yes | Yes | Native | Native |
| SDK diagnostic log callbacks | Yes | Yes | Yes | Native listener | Native listener |

Automatic connection registers host-local runtimes directly with a signed-in
host. Physical devices use the app-private registration created by their
first QR scan.

## Capture and diagnostics

Native crash capture is enabled by default in every mobile SDK and is owned by
the shared Kotlin/Swift core, including when called through .NET, React Native,
Capacitor, or Flutter.

| Crash capability | .NET / MAUI | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Native fatal hooks | Native bridge | JVM handler plus Android exit reasons | Objective-C exception, fatal signals, and MetricKit | Native bridge | Native bridge | Native bridge |
| Framework stack context | CLR unhandled exception | JVM throwable | Objective-C exception | Fatal JS global handler and rejection context | WebView error/rejection context | Flutter framework and platform-dispatcher context |
| Previous live-session handoff | Yes | Yes | Yes | Yes | Yes | Yes |
| Previous offline-capture attachment | Yes | Via the .NET host | Via the .NET host | — | — | — |
| Durable next-launch outbox | Yes | Yes | Yes | Yes | Yes | Yes |

| Capability | .NET / MAUI | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Periodic live JPEG capture | Yes | Yes | Yes | Native | Native | Native |
| Automatic screenshot-aligned or touch-triggered visual-tree capture | Yes | Yes | Yes | Native | Native | Native |
| On-demand screenshot (`ui.get_screenshot`; runtime helper where exposed) | Yes | Yes | Yes | Native | Native | Native |
| GPU-backed surface capture option | Accepted for parity | Accepted for parity | Yes | Native; meaningful on iOS | Native; meaningful on iOS | Native; meaningful on iOS |
| host-owned simulator/emulator screenshots | Yes | Yes | Yes | Native | Native | Native |
| Touch capture and runtime enable/disable | Yes | Yes | Yes | Native | Native | Native |
| Opt-in per-frame on-screen keyboard presence metadata | Yes | Yes | Yes | Native | Native | Native |
| Touch-capture app guard | Yes | Yes | Yes | Native toggle; JS policy is app-owned | Native toggle; JS policy is app-owned | Native toggle; Dart policy is app-owned |
| HTTP request capture | `HttpClient` handler plus manual API | Typed manual API | Typed manual API | Opt-in `fetch` / XHR | Opt-in `fetch` / XHR | `AnsightHttpClient` plus manual API |
| App-configurable network sanitizer | Yes | Native mandatory policy | Native mandatory policy | JavaScript hook, then native policy | JavaScript hook, then native policy | Dart hook, then native policy |
| Native visual-tree providers | Yes | Yes | Yes | Native plus a React provider | Native plus a DOM provider | Native |
| App-provided visual-tree sources | Yes | Yes | Yes | Native code can register providers; React inspection uses separate `react.*` tools | Native hierarchy plus `dom.*` WebView tools | Native code can register providers |

The order of a node's `children` records its structural sibling order. A node
with a non-default stacking override may additionally include a `z`
number. Platform adapters normalize their effective stacking value into that
field and omit it at the default. The paired screenshot remains the
authoritative record of final occlusion.

Visual-tree payloads store runtime type names once in the top-level `types`
array. Every node references that registry with a required `typeId`; node-local
`type`, `kind`, and `styleId` fields are not part of the compact format.

Session JPEG capture has three modes. `screenshotOnly` sends no automatic
visual trees. `screenshotAndVisualTree` captures a tree alongside each SDK
screenshot. `screenshotWithVisualTreeOnTouch` keeps screenshots on their normal
schedule but captures a tree only on touch down and touch up. Move and cancel
events do not trigger capture. Touch-triggered trees use a bounded latest-value
queue and are rate-limited to at most the screenshot cadence (with a 750 ms
minimum interval). Rapid boundaries are coalesced, prioritizing the latest
gesture-start snapshot, so tree traversal cannot starve periodic screenshots.
The touch mode requires touch capture and at least one registered visual-tree
provider.

Periodic screenshot producers use start-to-start deadlines and skip missed
deadlines instead of issuing catch-up bursts. Runtimes with asynchronous
WebSocket delivery keep only the latest unsent frame so network backpressure
drops stale evidence rather than slowing future capture.

During `device.profile`, current runtimes advertise screenshot-control version
1. host can respond with host capture mode for a simulator or emulator. The
SDK then suspends its periodic in-app JPEG loop for that session so host can
use `simctl`, `adb`, or another host-side capture source. If host does not
request host capture, the configured SDK JPEG capture continues.

Screen capture adds rendering, encoding, and transport work. Disable periodic
capture for performance-focused measurements unless visual evidence is needed.

## Remote tools and extensibility

All remote tools use the shared `tool.query` / `tool.call` protocol and are
subject to the configured tool guard. Read-only, read/write, and full-access
guards progressively expose `Read`, `Write`, and `Delete` scopes.

| Suite or extension | .NET / MAUI | Android | iOS | React Native | Capacitor |
| --- | --- | --- | --- | --- | --- |
| Custom tool registration and schemas | Yes | Yes | Yes | JavaScript and native | JavaScript and native |
| Runtime feature package initialization | `IRuntimeFeature` | No | No | No | No |
| App artifact providers and binary export | Yes | Yes | Yes | JavaScript provider with native binary transport | JavaScript provider with native binary transport |
| Visual tree, screenshot, inspection, and overlays (`ui.*`) | Yes | Yes | Yes | Native | Native |
| Sandboxed files and binary download (`files.*`) | Yes | Yes | Yes | Native | Native |
| File-descriptor diagnostics (`file_descriptors.*`) | No | Yes | Yes | Native aggregate defaults | Native aggregate defaults |
| JNI reference graph (`jni_references.capture_graph`) | Android target | Yes | No | Android native bridge | Android native bridge |
| Preferences (`prefs.*`) | Yes | Yes | Yes | Native | Native |
| Secure storage (`secure.*`) | Yes | Yes | Yes | Native | Native |
| SQLite discovery/schema/read query (`data.*`) | Yes | Yes | Yes | Native | Native |
| Registered-root reflection (`reflect.*`) | Yes | Yes | Yes | Native | Native |
| .NET MAUI inspection and mutation (`maui.*`) | Framework | No | No | No | No |
| React component/shadow tree and actions (`react.*`) | No | No | No | Framework | No |

App artifact providers advertise dynamically available exports through
`artifacts.query` and create one requested snapshot through
`artifacts.request`. Text, bytes, streams, and app-local files are supported
according to the platform API. Requested bytes are transferred over the live
binary file-transfer channel, so artifact creation requires an active host
session.

## Framework and workflow features

| Feature | Availability | Package or entry point |
| --- | --- | --- |
| .NET all-in-one developer defaults and native tools | .NET | `Ansight` / `WithAnsightSdk(...)` |
| MAUI bootstrap, automatic lifecycle, and page-view telemetry | .NET MAUI | `Ansight.Maui` / `UseAnsight<App>()` |
| MAUI UI inspection, XAML inflation, mutation, resources, bindings, navigation, layout, and handler diagnostics | .NET MAUI | `Ansight.Tools.Maui` |
| Annotated in-app feedback with screenshots, all visual-tree sources, hooks, artifacts, outbox, and live/offline delivery | .NET Android, iOS, and Mac Catalyst Debug app builds | `Ansight.Annotations` / `WithAnnotatedFeedback()` |
| Offline telemetry, events, touches, screenshots, annotation bundles, retention, ZIP/AES export, and team upload | .NET | `Ansight.OfflineCapture` |
| Native crash capture, prior-session association, host handoff, and offline capture attachment | All mobile SDKs; offline attachment currently uses `.NET` Offline Capture | Core runtime `crashCapture` options |
| Objective-C facade | iOS | `AnsightObjC` |
| React component and shadow-tree inspection | React Native | `installReactTools(...)` |
| React Navigation route tracking | React Native | `createReactNavigationTracker(...)` |
| Unhandled JavaScript error and rejection capture | React Native | `installErrorHandlers(...)` |
| WebView DOM inspection, queries, and guarded actions | Capacitor | `withDomTools(...)` / `installDomTools(...)` |
| Browser route and lifecycle tracking | Capacitor | `createRouteTracker(...)` / `startLifecycleTracking()` |
| JavaScript error and rejection capture | Capacitor | `withErrorCapture(...)` / `installErrorHandlers(...)` |
| Flutter widget-tree inspection and actions | Flutter | `AnsightFlutterInstrumentation.instance.install()` |
| Flutter route and screen-view tracking | Flutter | `AnsightNavigatorObserver` |
| Flutter error, frame-timing, and lifecycle capture | Flutter | `AnsightFlutterInstrumentation.instance.install()` |
| Dart custom tools and app artifacts | Flutter | `Ansight.instance.registerTool(...)` / `registerArtifactProvider(...)` |

`Ansight` and `Ansight.Maui` reference the annotation and offline-capture
packages, but neither workflow starts automatically. Annotated feedback must be
explicitly enabled and is hard-disabled in Release application builds. Offline
capture starts only after the app configures and initializes an
`OfflineCaptureController`.

## Build-time safeguards

| Capability | .NET / MAUI | Android | iOS | React Native | Capacitor |
| --- | --- | --- | --- | --- | --- |
| Simulator/emulator/desktop enrollment | Install + initialize | Install + initialize | Install + initialize | Install + initialize | Install + initialize |
| Physical-device enrollment | Install + scan once | Install + scan once | Install + scan once | Install + scan once | Install + scan once |
| Detect bundled remote-tool implementations | `AnsightRemoteToolsPolicy` | No SDK build scanner | Build tool; requires `ANSIGHT_ALLOW_REMOTE_TOOLS=true` | Native build rules apply | Native build rules apply |
| Hard-disable annotated feedback in Release builds | Yes | Not available | Not available | Not available | Not available |

Enrollment UI and broad remote-tool access are development features. Do not
ship unrestricted tool policies in CI, store, TestFlight, Play Store, or other
distributable builds.

## Detailed guides

- [.NET SDK](../src/dotnet/README.md)
- [Android SDK](../src/android/README.md)
- [iOS SDK](../src/ios/README.md)
- [React Native SDK](../src/react-native/README.md)
- [Capacitor SDK](../src/capacitor/README.md)
- [Flutter SDK](../src/flutter/README.md)
- [Flutter open-source corpus](../src/flutter/validation/flutter-corpus-results.md)
- [.NET annotated feedback](../src/dotnet/Ansight.Annotations/README.md)
- [.NET offline capture](../src/dotnet/Ansight.OfflineCapture/README.md)
- [.NET MAUI tools](../src/dotnet/Ansight.Tools.Maui/README.md)
