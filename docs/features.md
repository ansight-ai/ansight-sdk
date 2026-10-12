# Ansight SDK Feature Catalog

This catalog describes the implementation in this repository, reviewed against
runtime code, framework bridges, tool registrations, and package/build files on
2026-10-13. Availability means an implementation exists; it does **not** mean the
feature is enabled by default or that every platform implements it identically.
Use [Cross-SDK API Parity](sdk-api-parity.md) for API naming and
[Protocol](protocol.md) for wire contracts. Implementation links below identify
the source of truth when another guide disagrees.

Unless a row says otherwise, the matrices describe Android/iOS apps. The .NET
column also covers the corresponding Mac Catalyst implementation where available;
it does not promise native UI or crash capture for plain `net9.0` desktop apps.
React Native and Capacitor use Android/iOS bridges. Flutter registers Android,
iOS, and macOS plugins; its macOS capture limitations are described separately.
The Swift package also targets macOS, but UIKit-dependent features do not become
AppKit features merely because the package builds there.

Legend:

- **Yes**: implemented in that SDK; configuration or package registration may be required.
- **Native**: supplied through the Kotlin/Swift runtime bridge.
- **Framework**: supplied by the named framework integration.
- **No**: no implementation exposed by that SDK for this capability.

Platform declarations: [.NET package](../src/dotnet/Ansight/Ansight.csproj),
[Swift package](../src/ios/Package.swift), [Flutter plugin](../src/flutter/pubspec.yaml).

## Runtime and connection

| Capability | .NET / MAUI | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Initialize, activate, deactivate, and clear | Yes | Yes | Yes | Native | Native | Native |
| Custom metric channels and metric/event recording | Yes | Yes | Yes | Native | Native | Native |
| Periodically sampled app-defined metric streams | App schedules `Runtime.Metric(...)` | `AnsightMetricStream` | `AnsightMetricStream` | No JavaScript sampler API | No JavaScript sampler API | No Dart sampler API |
| FPS, memory, and optional battery sampling | Yes | Yes | Yes | Native plus optional JS heap profiling | Native | Native plus Flutter frame timings after instrumentation install |
| Optional open-file-handle metrics | Yes, platform-dependent | Yes | Yes | Native | Native | Native |
| Optional JNI-reference-count metrics | Android target | Yes | No | Android native | Android native | Android native |
| Screen-view and app-lifecycle events | Yes; MAUI lifecycle/page integration | Activity lifecycle/screen tracking | UIKit lifecycle/view-controller tracking | Native plus AppState and React Navigation helpers | Native plus WebView lifecycle and route helpers | Native plus Flutter lifecycle and navigator helpers |
| Retained metric/event readback and debug snapshots | Yes | Yes | Yes | Native | Native | Native |
| Device/app profile reporting | Yes | Yes | Yes | Native plus framework properties | Native plus framework properties | Native plus framework properties |
| Live telemetry, events, touches, screenshots, and control messages | Yes | Yes | Yes | Native | Native | Native |
| App-provided client logs | Yes | Yes | Yes | Native | Native | Native |
| Grouped session/custom properties and live updates | Yes | Yes | Yes | Native | Native | Native |
| App-private enrollment registration | Yes | Yes | Yes | Native | Native | Native |
| Host-local enrollment and automatic probing | Yes | Emulator | Simulator | Native | Native | Native; also macOS |
| Remembered host profiles and explicit payload connection | Yes | Yes | Yes | Native | Native | Native |
| SDK enrollment UI | Platform pairing package | QR scanner | UIKit QR scanner | Native QR bridge | Native QR bridge | Native QR bridge on Android/iOS |
| Connection status, capabilities, and change listeners | Yes | Yes | Yes | Native listener | Native listener | Native stream |
| SDK diagnostic log callbacks | Yes | Yes | Yes | Native listener | Native listener | Native stream |
| Opt-in connectivity observations | `Ansight.Connectivity` | Native monitor | `NWPathMonitor` | Native | Native | Native |

Host-local enrollment requires an active runtime, automatic probing (or an
explicit connection attempt), and a reachable compatible host. The SDK sends
local enrollment requests; host authentication/acceptance is host behavior.
Physical devices normally obtain their saved registration through the first QR
scan. QR UI also requires the platform pairing integration and an available
foreground activity/view controller.

Connectivity capture records network availability, transport changes, and
available platform quality metadata. It is separate from HTTP request capture;
it does not perform speed tests or active reachability probes. Motion capture is
also opt-in: the SDK accepts app-fed shake and accelerometer samples through
`Ansight.Motion`, `AnsightMotion`, or bridge methods; it does not install a general
accelerometer subscription for the app.

Developer presets differ. `.NET WithAnsightSdk`, Android `withAnsightSdk`, and
Swift `ansightDeveloperDefaults` grant full tool access. React Native/Capacitor
`withAnsightDefaults` and Flutter `AnsightOptions.developer()` use read-only
access; their `withAnsightSdk` builders opt into full access. Bare initialization
is not interchangeable with those presets.

Sources: [.NET runtime](../src/dotnet/Ansight.Core/RuntimeImpl.cs),
[Android runtime](../src/android/ansight-core/src/main/kotlin/ai/ansight/runtime/AnsightRuntime.kt),
[Swift runtime](../src/ios/Sources/AnsightCore/AnsightRuntime.swift),
[React Native API](../src/react-native/index.js),
[Capacitor API](../src/capacitor/src/index.ts),
[Flutter API](../src/flutter/lib/src/ansight_runtime.dart),
[connectivity monitor](../src/ios/Sources/AnsightCore/AnsightConnectivityCapture.swift),
[motion facade](../src/dotnet/Ansight.Motion/MotionCapture.cs).

## Capture and diagnostics

### Crashes and framework errors

Native crash capture defaults to enabled in the Kotlin/Swift runtime and can be
disabled with crash-capture options. Framework bridges reuse that implementation.
Framework error hooks require their own installation and do not turn every
nonfatal error or rejected promise into a fatal crash report.

| Crash capability | .NET / MAUI | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Native fatal evidence | Native bridge | JVM uncaught handler, native fatal signals, Android exit reasons | Objective-C exception handler, fatal signals, MetricKit | Native | Native | Native |
| Framework context | CLR unhandled-exception hook | JVM throwable | Objective-C exception | `installErrorHandlers`; fatal JS handler and nonfatal rejection context | Opt-in WebView error/rejection context, recorded as nonfatal | Instrumentation error hooks, recorded as nonfatal |
| Prior-session association and next-launch outbox | Yes, native targets | Yes | Yes | Native | Native | Native |
| Handoff to a connected host | Yes | Yes | Yes | Native | Native | Native |
| Crash files attached to managed offline archives | `Ansight.OfflineCapture` | Through a .NET app using that package | Through a .NET app using that package | No managed archive integration | No managed archive integration | No managed archive integration |

Android exit-history recovery requires API 30+ and includes available ANR,
low-memory, and other qualifying process-exit evidence. Signal capture also has
a native C implementation. Apple MetricKit evidence depends on platform/API
availability and delivery. Native offline sessions associate their identity with
the crash recorder, but their crash delivery remains the separate crash outbox;
the managed offline controller implements crash-file attachment to its archives.

Sources: [Android crash recorder](../src/android/ansight-core/src/main/kotlin/ai/ansight/runtime/AnsightCrashCapture.kt),
[Android signal handler](../src/android/ansight-core/src/main/cpp/ansight_crash_signal.c),
[Apple crash recorder](../src/ios/Sources/AnsightCore/AnsightCrashCapture.swift),
[managed crash hook](../src/dotnet/Ansight.Core/Runtime.cs).

### Screenshots, input, and visual trees

| Capability | .NET / MAUI | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Periodic live JPEG capture | Native on mobile | Yes | Yes | Native | Native | Native on mobile; boundary on macOS |
| Automatic screenshot-aligned or touch-triggered trees | Yes | Yes | Yes | Native plus installed React provider | Native plus installed DOM provider | Native plus installed Flutter provider; macOS caveat below |
| On-demand screenshot (`ui.get_screenshot`; helper where exposed) | Yes | Yes | Yes | Native | Native | Native on mobile; Flutter capture controller on macOS |
| GPU-backed surface capture | Native Apple option; Android native renderer | PixelCopy/surface capture where available; option is not a separate switch | GPU-aware renderer controlled by option | Native | Native | Native on mobile; compositor boundary on macOS |
| Host screenshot-control negotiation | Yes | Yes | Yes | Native | Native | Native |
| Touch capture and runtime enable/disable | Yes | Yes | Yes | Native | Native | Native on mobile; boundary pointer forwarding on macOS |
| Stylus/eraser/Pencil samples, with available pressure, angles, and hover | Native | Yes | Yes | Native | Native | Native on mobile |
| Opt-in per-frame keyboard-presence metadata | Yes | Yes | Yes | Native | Native | Native on mobile |
| App callback guarding touch capture | Yes | Yes | Yes | Native toggle; JS callback policy is app-owned | Native toggle; JS callback policy is app-owned | Native toggle; Dart callback policy is app-owned |
| Native hierarchy provider | Yes | Yes | Yes | Native | Native | Native |
| App-registered visual-tree providers | Yes | Yes | Yes | Native registration; installed React adapter | Native registration; installed DOM adapter | Native registration; installed Flutter adapter |

Installing `installReactTools(...)`, `installDomTools(...)`, or Flutter
instrumentation connects the framework tree to the native provider registry as
well as exposing framework-specific tools. These adapters also support the
common `ui.*` inspection/action path; action availability depends on the provider,
node, and guard. Native hierarchy inspection alone does not imply access to React
components, DOM nodes, or Flutter widgets.

Compact tree payloads use a top-level `types` array and node `typeId` references.
The order of `children` preserves structural sibling order; optional non-default
`z` values describe stacking overrides. Node-local `type`, `kind`, and `styleId`
are not the compact node format. The paired screenshot is the evidence of final
occlusion.

For native **live-session** capture, `screenshotOnly` omits automatic trees,
`screenshotAndVisualTree` captures them with screenshots, and
`screenshotWithVisualTreeOnTouch` captures them at gesture down/up boundaries.
Move/cancel samples do not request trees. The touch mode requires enabled touch
capture and a registered provider; its bounded queue coalesces boundaries,
prioritizes gesture starts, and enforces at least the configured screenshot
interval or 750 ms, whichever is longer.

The Swift live JPEG loop skips missed deadlines and keeps the latest unsent
frame under asynchronous delivery backpressure. Android uses a single-threaded
`scheduleAtFixedRate` capture/send loop; it does not implement the same explicit
missed-deadline skipping or latest-frame buffer. Capture scheduling is not
identical across runtimes. Rendering, encoding, and transport add measurement
overhead; periodic screenshots can be disabled for performance-focused work.

Flutter macOS capture requires `AnsightFlutterCaptureBoundary` and its controller.
The boundary captures the wrapped Flutter compositor and optionally its widget
tree, and forwards pointer samples. Its `automaticCaptureOptions` govern its own
loop: it waits the configured interval **after** each capture completes. The
implementation does not deduplicate unchanged frames. The boundary attaches
trees to frames only in `screenshotAndVisualTree` mode. Touch-triggered trees
instead require the native runtime's `sessionJpegCapture` touch mode and installed
provider; selecting that mode only in the boundary options does not configure
the native coordinator. Frame/pointer submission requires a connected live
session, and does not feed the native offline recorder. Native AppKit content
outside the boundary, UIKit screenshot tools, and the native annotation editor
are not supplied by this Flutter boundary.

The native runtimes advertise screenshot-control version 1 in the device profile.
A host can select host capture and suppress the SDK's periodic live JPEG loop.
The SDK implements negotiation/suppression; simulator/emulator screenshot tools
such as `simctl` and `adb` belong to the host, outside this SDK repository.

Sources: [Android renderer](../src/android/ansight-core/src/main/kotlin/ai/ansight/runtime/AndroidUiEvidence.kt),
[Apple renderer](../src/ios/Sources/AnsightCore/AnsightScreenSnapshotRenderer.swift),
[.NET native capture options](../src/dotnet/Ansight.Core/Native/NativeRuntimeOptionsJson.cs),
[React provider adapter](../src/react-native/ios/AnsightReactNative.swift),
[DOM provider adapter](../src/capacitor/ios/Sources/AnsightCapacitorPlugin/AnsightCapacitorPlugin.swift),
[Flutter instrumentation/boundary](../src/flutter/lib/src/ansight_flutter.dart),
[host capture policy](../src/ios/Sources/AnsightCore/HostSessionJpegCapturePolicy.swift).

### HTTP evidence

| Capability | .NET / MAUI | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Manual request recording | Yes | Typed/JSON API | Typed/JSON API | JS API | JS API | Dart API |
| Automatic or wrapped request capture | Apple native URL capture; otherwise `HttpClient` diagnostic listener; explicit `AnsightHttpMessageHandler` | No bundled general HTTP interceptor | URL Loading System `URLProtocol` implementation | Opt-in `fetch` / XHR wrappers | Opt-in `fetch` / XHR wrappers | Explicit `AnsightHttpClient` for `package:http` |
| App-configurable sanitization | Options and callbacks | Fixed native sanitizer | Native capture options and redaction toggle | JS sanitizer, then native handling | JS sanitizer, then native handling | Dart sanitizer, then native handling |
| Optional bounded request/response bodies | Explicit handler; native Apple capture | Manual payloads | Native URL capture/manual payloads | JS capture/manual payloads | JS capture/manual payloads | HTTP wrapper/manual payloads |

Automatic .NET capture defaults on for recognized Android emulators/iOS
simulators unless overridden. Its non-Apple diagnostic listener observes
metadata without reading bodies; Apple targets use native URL capture to avoid
duplicate observation. The Swift URL-capture option defaults on in simulator
builds and off elsewhere. It covers requests handled by its URL protocol, not
all networking stacks. Its task metrics can add time-to-first-byte, DNS,
connection, TLS, upload/download duration, byte-count, and connection-reuse
evidence for the final transaction when available; those extra fields are not
implemented in all request models.

JS wrappers and the Dart HTTP client capture when a live connection or native
offline recording destination is available. Body capture defaults on in these
capture options, with a default 64 KiB limit per body and separate binary-body
opt-in. Redaction is enabled by default. Apple and .NET expose redaction controls;
Android's native recording boundary always applies its sanitizer.

Sources: [.NET diagnostic listener](../src/dotnet/Ansight.Core/Network/AutomaticHttpClientNetworkCapture.cs),
[.NET explicit handler](../src/dotnet/Ansight.Core/Network/AnsightHttpMessageHandler.cs),
[Apple URL protocol](../src/ios/Sources/AnsightCore/AnsightNetworkURLProtocol.swift),
[Apple capture defaults](../src/ios/Sources/AnsightCore/AnsightNetworkCaptureOptions.swift),
[Android sanitizer](../src/android/ansight-core/src/main/kotlin/ai/ansight/runtime/AnsightNetworkRequestSanitizer.kt),
[Dart HTTP wrapper](../src/flutter/lib/src/ansight_network.dart).

## Remote tools and extensibility

Tools use `tool.query` / `tool.call`, support `tool.batch`, and publish the v3
tool catalog through the shared native/managed protocol implementations. `ReadOnly`,
`ReadWrite`, and `FullAccess` progressively allow **Read, Write, and Critical**
policies. There is no `Delete` policy. A registered tool may remain undiscoverable
or unavailable because of its guard, platform, missing provider, or runtime state.

| Suite or extension | .NET / MAUI | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Custom tools and schemas | Yes | Yes | Yes | JS and native | JS and native | Dart and native |
| Runtime feature initialization contract | `IRuntimeFeature` | No equivalent contract | No equivalent contract | No | No | No |
| App artifact providers and binary export | Yes | Yes | Yes | JS provider/native transport | JS provider/native transport | Dart provider/native transport |
| Tree, screenshot, node inspection, overlays (`ui.*`) | Yes | Yes | Yes | Native/framework provider | Native/framework provider | Native/framework provider |
| Query, actions, and waits (`ui.query_nodes`, `ui.perform_action`, `ui.wait`) | Yes | Yes | Yes | Through providers | Through providers | Through providers |
| Sandboxed files and binary transfers (`files.*`) | Yes | Yes | Yes | Native | Native | Native |
| Clipboard text read/presence/write/clear (`clipboard.*`) | Yes | Yes | Yes | Native | Native | Native |
| File-descriptor diagnostics (`file_descriptors.*`) | No managed suite | Yes | Yes | Native aggregate defaults | Native aggregate defaults | Native aggregate defaults |
| JNI graph (`jni_references.capture_graph`) | Android target | Yes | No | Android native | Android native | Android native |
| Preferences (`prefs.*`) | Yes | Yes | Yes | Native | Native | Native |
| Secure storage (`secure.*`) | Yes | Yes | Yes | Native | Native | Native |
| SQLite discovery/schema/read queries (`data.*`) | Yes | Yes | Yes | Native | Native | Native |
| Registered-root reflection (`reflect.*`) | Managed roots | JVM roots | Swift/ObjC roots | Native roots | Native roots | Native roots |
| Purchase evidence/validation (`purchases.*`, opt-in) | Core + Apple/Play adapters | Core + separate Play adapter | Core + StoreKit 2 | Native facade | Native facade | Native facade |
| Native offline transfer (`offline_capture.transfer`) | Native recorder | Yes | Yes | Native | Native | Native |
| MAUI inspection/mutation (`maui.*`) | Framework | No | No | No | No | No |
| React component/shadow tree/actions (`react.*`) | No | No | No | Framework | No | No |
| WebView DOM queries/actions (`dom.*`) | No | No | No | No | Framework | No |
| Flutter widgets/actions (`flutter.*`) | No | No | No | No | No | Framework |

Artifact providers list available exports through `artifacts.query` and produce
one snapshot through `artifacts.request`. Text, bytes, streams, and local-file
payloads vary by SDK API. Binary delivery requires an active host session.

Purchase diagnostics are passive evidence and expectation checks, restricted by
the implementation to recognized simulators/emulators. They do not purchase,
grant, acknowledge, consume, or finish transactions. The Android Google Play
adapter accepts the app's existing billing callbacks; it is a separate package,
not a billing client installed by the aggregate SDK. See [Purchase diagnostics](purchases.md).

Sources: [guard policies](../src/dotnet/Ansight.Core/Tools/ToolGuard.cs),
[Android aggregate tools](../src/android/ansight/src/main/kotlin/ai/ansight/AnsightOptionsBuilderExtensions.kt),
[Apple aggregate tools](../src/ios/Sources/Ansight/AnsightRemoteTools.swift),
[.NET aggregate tools](../src/dotnet/Ansight/AnsightOptionsBuilderExtensions.cs),
[common UI tools](../src/ios/Sources/AnsightToolsVisualTree/AnsightGenericUITools.swift),
[purchase environment gate](../src/dotnet/Ansight.Core/Purchases/PurchaseEnvironment.cs),
[Play observer](../src/android/ansight-purchases-googleplay/src/main/kotlin/ai/ansight/purchases/googleplay/GooglePlayPurchaseObserver.kt).

## Framework and workflow features

| Feature | Availability | Package or entry point |
| --- | --- | --- |
| All-in-one developer configuration | All six SDKs | Aggregate packages and SDK/default builders; guard defaults differ as above |
| MAUI bootstrap, lifecycle, page-view telemetry | .NET MAUI | `Ansight.Maui` / `UseAnsight<App>()` |
| MAUI XAML inflation, mutation, resources, bindings, navigation state, commands, layout, handler diagnostics | .NET MAUI | `Ansight.Tools.Maui` |
| In-app annotations with screenshot/tree evidence, durable retry outbox, and live delivery | Android/UIKit apps through native and framework bridges, subject to debug gating | `Annotate.PresentAsync()` / annotated-feedback options |
| Objective-C facade | Apple SDK | `AnsightObjC` |
| React Navigation route tracking | React Native | `createReactNavigationTracker(...)` |
| JS error/rejection hooks | React Native | `installErrorHandlers(...)` |
| WebView DOM inspection and guarded actions | Capacitor | `withDomTools(...)` / `installDomTools(...)` |
| Browser route and lifecycle tracking | Capacitor | `createRouteTracker(...)` / `startLifecycleTracking()` |
| WebView error/rejection hooks | Capacitor | `withErrorCapture(...)` / `installErrorHandlers(...)` |
| Widget inspection/actions, errors, frame timings, lifecycle | Flutter | `AnsightFlutterInstrumentation.instance.install()` |
| Flutter route/screen tracking | Flutter | `AnsightNavigatorObserver` with instrumentation installed |
| Flutter macOS live visual capture | Flutter macOS | `AnsightFlutterCaptureBoundary` / `AnsightFlutterCaptureController` |
| .NET diagnostic-port build setup and application-ready signal | .NET Android/iOS | `Ansight.Profiling.DotNet`, `AnsightProfilingEnabled`, `AnsightProfiler.ApplicationReady()` |

Annotation presentation requires initialization, enabled options, a supported
native editor, and the applicable debug-build check. Including the package alone
does not make presentation available on every target. The .NET profiling package
configures diagnostic endpoints and emits a one-time startup event; trace
collection and analysis require external tooling.

Sources: [MAUI tool IDs](../src/dotnet/Ansight.Tools.Maui/MauiToolIds.cs),
[Android annotations](../src/android/ansight-core/src/main/kotlin/ai/ansight/runtime/Annotate.kt),
[Apple annotations](../src/ios/Sources/AnsightCore/Annotate.swift),
[profiling build integration](../src/dotnet/Ansight.Profiling.DotNet/buildTransitive/Ansight.Profiling.DotNet.targets),
[startup signal](../src/dotnet/Ansight.Profiling.DotNet/AnsightProfiler.cs).

### Offline recording

There are two recording implementations:

- **Native recorder:** Kotlin/Swift core, exposed through .NET, React Native,
  Capacitor, and Flutter options. Opt in with `WithOfflineCapture(...)` /
  `withOfflineCapture(...)` or `offlineCapture.enabled`. While the runtime is
  active, recording is enabled, and no live session is connected, it retains
  metrics, events, touches, client logs, HTTP evidence, session properties,
  configured screenshots, and registered trees. It has bounded queues/storage,
  recovery, session retention, and automatic/manual host transfer. Automatic
  transfer starts on connection; manual transfer uses `offline_capture.transfer`.
  Local sessions are purged only after a matching durable host receipt. A
  compatible host receiver is required.
- **Managed recorder:** `Ansight.OfflineCapture` / `OfflineCaptureController`.
  Implements telemetry/events/touches/network evidence, screenshots, annotation
  bundles, crash attachment, retention, and ZIP export with optional AES-256.
  Team upload is not implemented in this repository's offline-capture package.

These are distinct archives and lifecycles; do not enable both recorders for the
same intended recording. Native offline screenshot loops use a delay after
capture, and both non-`screenshotOnly` modes capture trees with each offline
screenshot. They do not use the live touch-only tree scheduling described above.
Flutter macOS boundary frames/pointers are live-only; the existence of native
macOS offline storage does not supply offline Flutter compositor capture.
Annotations use their own native outbox; managed annotation-bundle attachment is
an additional managed-recorder integration.

Sources: [Android offline coordinator](../src/android/ansight-core/src/main/kotlin/ai/ansight/runtime/offline/NativeOfflineCapture.kt),
[Apple offline coordinator](../src/ios/Sources/AnsightCore/OfflineCapture/NativeOfflineCapture.swift),
[Apple store](../src/ios/Sources/AnsightCore/OfflineCapture/OfflineCaptureStore.swift),
[managed controller](../src/dotnet/Ansight.OfflineCapture/OfflineCaptureController.cs),
[managed ZIP exporter](../src/dotnet/Ansight.OfflineCapture/OfflineCaptureZipExporter.cs).
See also [Native offline capture](native-offline-capture-implementation.md).

## Build-time safeguards

| Capability | Implemented behavior |
| --- | --- |
| .NET remote-tool assembly scan | `AnsightRemoteToolsPolicy`: defaults to `AllowedWithWarnings`; `Disallowed` fails on detected tools; `Allowed` bypasses scanning. This is not an automatic Release prohibition. |
| Apple source scanner | `AnsightBuildTool` scans the selected target's Swift sources for concrete `AnsightTool` conformers and requires `ANSIGHT_ALLOW_REMOTE_TOOLS` when it finds them. It is not a whole-app binary scan. |
| Apple build integration | CocoaPods runs the scanner for `AnsightCore`. Both SwiftPM manifests declare `AnsightBuildToolPlugin` but do not attach it to SDK targets. Scanner existence does not imply enforcement on every SwiftPM build. |
| Android remote-tool scanner | No SDK build scanner. Runtime tool guards still apply. |
| .NET annotation gating | App assembly metadata/debug attributes determine whether presentation is enabled; the build-transitive target writes true for Debug and false otherwise. |
| Android annotation gating | Runtime checks the app's `FLAG_DEBUGGABLE` and annotation options. |
| Apple annotation gating | Uses `#if DEBUG`, with an explicit `debugBuildOverride` for bridges. This is a runtime presentation gate, not unconditional removal from Release binaries. |
| React Native, Capacitor, Flutter | Inherit the corresponding native build integration and annotation checks; no separate universal framework scanner. |

Broad tool policies are development settings. Configure distributable builds
explicitly; inclusion of an aggregate package is not proof that remote tools have
been stripped or disabled.

Sources: [.NET scan target](../src/dotnet/Ansight.Core/build/Ansight.targets),
[Apple scanner](../src/ios/Plugins/AnsightBuildTool/AnsightBuildToolMain.swift),
[CocoaPods integration](../src/ios/AnsightCore.podspec),
[root SwiftPM manifest](../Package.swift), [nested SwiftPM manifest](../src/ios/Package.swift),
[.NET annotation build policy](../src/dotnet/Ansight.Annotations/AnnotationBuildPolicy.cs),
[Apple annotation options](../src/ios/Sources/AnsightCore/AnnotationOptions.swift).

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
- [.NET profiling integration](../src/dotnet/Ansight.Profiling.DotNet/README.md)
- [Connectivity capture](connectivity.md)
- [Motion capture](motion.md)
- [Clipboard tools](clipboard-tools.md)
- [Network performance evidence](network-performance.md)
- [Purchase diagnostics](purchases.md)
