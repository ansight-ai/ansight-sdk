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
| Saved pairing config | Yes | Yes | Yes | Native | Native |
| Remembered host profiles and host auto-probe | Yes | Yes | Yes | Native | Native |
| Bundled and developer pairing sources | Yes | Yes | Yes | Native configuration | Native configuration |
| Explicit payload connection | Yes | Yes | Yes | Yes | Yes |
| SDK-owned pairing UI | Android/iOS QR via `Ansight.Pairing` | QR scanner and pairing sheet | UIKit file import and QR scanner | No; pass an app-acquired payload to `connect(...)` | No; pass an app-acquired payload to `connect(...)` |
| Connection status, capabilities, and change listeners | Yes | Yes | Yes | Native | Native |
| SDK diagnostic log callbacks | Yes | Yes | Yes | Native listener | Native listener |

Automatic connection uses the same source priority across SDKs: bundled
developer config, remembered host profiles newest-first, saved config, then the
plain bundled config.

## Capture and diagnostics

| Capability | .NET / MAUI | Android | iOS | React Native | Capacitor |
| --- | --- | --- | --- | --- | --- |
| Periodic live JPEG capture | Yes | Yes | Yes | Native | Native |
| On-demand screenshot (`ui.get_screenshot`; runtime helper where exposed) | Yes | Yes | Yes | Native | Native |
| GPU-backed surface capture option | Accepted for parity | Accepted for parity | Yes | Native; meaningful on iOS | Native; meaningful on iOS |
| Studio-owned simulator/emulator screenshots | Yes | Yes | Yes | Native | Native |
| Touch capture and runtime enable/disable | Yes | Yes | Yes | Native | Native |
| Touch-capture app guard | Yes | Yes | Yes | Native toggle; JS policy is app-owned | Native toggle; JS policy is app-owned |
| Native visual-tree providers | Yes | Yes | Yes | Native plus a React provider | Native plus a DOM provider |
| App-provided visual-tree sources | Yes | Yes | Yes | Native code can register providers; React inspection uses separate `react.*` tools | Native hierarchy plus `dom.*` WebView tools |

During `device.profile`, current runtimes advertise screenshot-control version
1. Studio can respond with host capture mode for a simulator or emulator. The
SDK then suspends its periodic in-app JPEG loop for that session so Studio can
use `simctl`, `adb`, or another host-side capture source. If Studio does not
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
binary file-transfer channel, so artifact creation requires an active Studio
session.

## Framework and workflow features

| Feature | Availability | Package or entry point |
| --- | --- | --- |
| .NET all-in-one developer defaults and native tools | .NET | `Ansight` / `WithAnsightSdk(...)` |
| MAUI bootstrap, automatic lifecycle, and page-view telemetry | .NET MAUI | `Ansight.Maui` / `UseAnsight<App>()` |
| MAUI UI inspection, XAML inflation, mutation, resources, bindings, navigation, layout, and handler diagnostics | .NET MAUI | `Ansight.Tools.Maui` |
| Annotated in-app feedback with screenshots, all visual-tree sources, hooks, artifacts, outbox, and live/offline delivery | .NET Android, iOS, and Mac Catalyst Debug app builds | `Ansight.Annotations` / `WithAnnotatedFeedback()` |
| Offline telemetry, events, touches, screenshots, annotation bundles, retention, ZIP/AES export, and team upload | .NET | `Ansight.OfflineCapture` |
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
| Generate and embed developer pairing material | MSBuild target; requires a signed source pairing JSON | App/build supplies bundled JSON | SwiftPM/CocoaPods build tool | Configure through the native projects or JS options | Configure through the native projects or JS options |
| Detect bundled remote-tool implementations | `AnsightRemoteToolsPolicy` | No SDK build scanner | Build tool; requires `ANSIGHT_ALLOW_REMOTE_TOOLS=true` | Native build rules apply | Native build rules apply |
| Hard-disable annotated feedback in Release builds | Yes | Not available | Not available | Not available | Not available |

Developer pairing material and broad remote-tool access are development
features. Do not embed developer pairing resources or unrestricted tool
policies in CI, store, TestFlight, Play Store, or other distributable builds.

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
