# Ansight SDK API Parity

This guide is the cross-SDK reference for public Ansight runtime APIs. The .NET
SDK remains the naming and behavior baseline; Android, iOS, React Native,
Capacitor, and Flutter should expose the same concepts with platform-native
conventions.

See [Current Feature Catalog](features.md) for a higher-level availability
matrix, including framework-specific and .NET-only workflows.

## Package Map

| Capability | .NET | Android | iOS | React Native | Capacitor |
| --- | --- | --- | --- | --- | --- |
| Core runtime | `Ansight.Core` | `ai.ansight:ansight-core-android` | `AnsightCore` | Native dependency | Native dependency |
| All-in-one runtime | `Ansight` | `ai.ansight:ansight-android` | `Ansight` | `@ansight/react-native` | `@ansight/capacitor` |
| Pairing UI | `Ansight.Pairing` | `ai.ansight:ansight-pairing-android` | `AnsightPairingQR` | Native QR bridge | Native QR bridge |
| App artifact providers | `Ansight.Core` | `ai.ansight:ansight-core-android` | `AnsightCore` | JavaScript plus native bridge | JavaScript plus native bridge |
| Visual tree tools | `Ansight.Tools.VisualTree` | `ai.ansight:ansight-tools-visualtree-android` | `AnsightToolsVisualTree` | Native tools plus React tools | Native tools plus DOM tools |
| File tools | `Ansight.Tools.FileSystem` | `ai.ansight:ansight-tools-filesystem-android` | `AnsightToolsFileSystem` | Native bridge | Native bridge |
| File descriptor diagnostics | — | `ai.ansight:ansight-tools-filedescriptordiagnostics-android` | `AnsightToolsFileDescriptorDiagnostics` | Native aggregate defaults | Native aggregate defaults |
| JNI reference diagnostics | `Ansight.Core` (Android target) | `ai.ansight:ansight-tools-jnireferencediagnostics-android` | — | Android native bridge | Android native bridge |
| Preferences tools | `Ansight.Tools.Preferences` | `ai.ansight:ansight-tools-preferences-android` | `AnsightToolsPreferences` | Native bridge | Native bridge |
| Secure storage tools | `Ansight.Tools.SecureStorage` | `ai.ansight:ansight-tools-securestorage-android` | `AnsightToolsSecureStorage` | Native bridge | Native bridge |
| Database tools | `Ansight.Tools.Database` | `ai.ansight:ansight-tools-database-android` | `AnsightToolsDatabase` | Native bridge | Native bridge |
| Reflection tools | `Ansight.Tools.Reflection` | `ai.ansight:ansight-tools-reflection-android` | `AnsightToolsReflection` | Native bridge | Native bridge |
| MAUI integration | `Ansight.Maui` | — | — | — | — |
| MAUI tools | `Ansight.Tools.Maui` | — | — | — | — |
| Annotated feedback | `Ansight.Annotations` | — | — | — | — |
| Offline capture | `Ansight.OfflineCapture` | — | — | — | — |
| Objective-C facade | — | — | `AnsightObjC` | Used by the iOS bridge | Used by the iOS bridge |
| React inspection tools | — | — | — | `@ansight/react-native` | — |

The file-descriptor suite is currently native-only; there is no
`Ansight.Tools.FileDescriptorDiagnostics` NuGet package in this repository.

Flutter applications use the `ansight_flutter` package. It consumes the
`Ansight` iOS product and `ai.ansight:ansight-android` artifact through its
native plugin bridges, while Dart APIs provide framework instrumentation,
custom tools, and artifact providers.
React Native receives the suite through the native aggregate defaults but does
not currently expose suite-specific JavaScript options.

## .NET Native Ownership

The .NET mobile targets use the same native runtime implementations as the
Android and iOS SDKs. `Ansight.Core` brings the internal Android or Apple
binding transitively; an app does not configure a bridge package.

| Responsibility | .NET Android | .NET iOS / Mac Catalyst |
| --- | --- | --- |
| Runtime and telemetry buffer | Kotlin runtime | Swift runtime |
| Enrollment and secure saved registration | Kotlin runtime | Swift runtime |
| Host auto-connect and reconnect | Kotlin runtime | Swift runtime |
| WebSocket, telemetry streaming, JPEG/touch capture, and binary transfer | Kotlin runtime | Swift runtime |
| C# API, CLR heap sample, MAUI hooks, and managed tool execution | .NET facade over native runtime | .NET facade over native runtime |
| `IDataSink` reads and update events | Projection of native buffer | Projection of native buffer |

This is a single-owner design: managed tools execute through a callback from the
native tool protocol, then native code sends the response on its existing
session. The .NET facade does not start a second pairing connection.

## All-In-One Defaults

The all-in-one/developer preset is intended to be equivalent across .NET,
Android, and iOS:

| Setting | Value |
| --- | --- |
| Sampling | 400 ms |
| Retention | 120 seconds |
| FPS | Enabled |
| Battery | Disabled |
| Open file handle tracking | Disabled; opt-in native sampling on Android and Apple platforms |
| JNI reference count tracking | Disabled; opt-in on Android when the integration can supply a tracked count |
| JPEG capture | Enabled, 2000 ms, quality 60, max width 480; iOS GPU-backed surface capture defaults to enabled |
| Touch capture | Enabled |
| Crash capture | Enabled; 8 pending reports, 7-day retention, 64 breadcrumbs, 1 MiB trace limit |
| Host auto-probe | Enabled |
| Tool guard | Full access in native all-in-one presets |
| Enrollment reconnect | Registers host-local runtimes automatically; physical devices reconnect after one QR scan |
| Standard tools | Registered by aggregate/all-in-one packages |

React Native defaults to core mode unless `useNativeAllInOneDefaults: true` is
passed or `withAnsightDefaults()` / `withAnsightSdk(...)` is used. That option
only applies the native all-in-one defaults; it is not build-type detection and
does not enable the entire feature set. `toolGuard`, capture options, host
auto-probe, and host connection remain separate controls.

> **Important:** Screen capture will result in an FPS drop while frames are
> rendered or captured, encoded, and transported. Keep periodic JPEG capture
> scoped to development or QA sessions, and disable it for performance-focused
> runs unless visual evidence is required.

## Runtime Diagnostic Channel Parity

`Open File Handles` uses reserved channel 7 and is sampled by the native
Android and Apple runtimes. `JNI reference count` uses reserved channel 6; JNI
does not expose a process-wide count, so .NET Android supplies Java.Interop's
tracked global-reference count to the native telemetry runtime. Both channels
are disabled by default.

| Operation | .NET | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Enable open handles | `WithOpenFileHandleTracking()` | `withOpenFileHandleTracking()` | `withOpenFileHandleTracking()` | `withOpenFileHandleTracking()` | `withOpenFileHandleTracking()` | `withOpenFileHandleTracking()` |
| Disable open handles | `WithoutOpenFileHandleTracking()` | `withoutOpenFileHandleTracking()` | `withoutOpenFileHandleTracking()` | `withoutOpenFileHandleTracking()` | `withoutOpenFileHandleTracking()` | `withoutOpenFileHandleTracking()` |
| Enable JNI count | `WithJniReferenceCountTracking()` | `withJniReferenceCountTracking()` | — | `withJniReferenceCountTracking()` | `withJniReferenceCountTracking()` | `withJniReferenceCountTracking()` |
| Disable JNI count | `WithoutJniReferenceCountTracking()` | `withoutJniReferenceCountTracking()` | — | `withoutJniReferenceCountTracking()` | `withoutJniReferenceCountTracking()` | `withoutJniReferenceCountTracking()` |

## Network Capture Parity

Network request V1 is a shared, metadata-only model. Request and response
bodies are not represented and are never read for capture.

| Concept | .NET | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Automatic integration | `AnsightHttpMessageHandler` | App/client integration | App/client integration | Opt-in `fetch` and XHR | Opt-in `fetch` and XHR | `AnsightHttpClient` |
| Manual record | `Runtime.RecordNetworkRequest(...)` | `AnsightRuntime.recordNetworkRequest(...)` | `AnsightRuntime.shared.recordNetworkRequest(...)` | `recordNetworkRequest(...)` | `recordNetworkRequest(...)` | `Ansight.instance.recordNetworkRequest(...)` |
| App sanitizer | `NetworkRequestSanitizer` options/callback | — | — | `AnsightNetworkSanitizationOptions` | `AnsightNetworkSanitizationOptions` | `AnsightNetworkSanitizationOptions` |
| Native boundary | Typed Kotlin model | Typed Kotlin model | Typed Swift model | Object bridge to Kotlin/Swift model | Object bridge to Kotlin/Swift model | Typed Pigeon model to Kotlin/Swift model |

JavaScript and Dart sanitizers can add sensitive names, rewrite URLs or whole
records, and suppress capture by returning `null`. Executable callbacks remain
in the app runtime; the data model crosses the bridge. Android/iOS then apply a
mandatory standard sanitizer and size bounds before transport, and the host
sanitizes again before writing `network/requests/`.

## Crash Capture Parity

All mobile surfaces use one native crash outbox. The fatal path performs only
a bounded app-private write; report construction and network delivery happen
after the next launch. Non-fatal framework candidates are retained as context
but are not promoted to crash reports without a fatal framework marker or
independent OS termination evidence.

| Concept | .NET | Android | iOS | React Native | Capacitor | Flutter |
| --- | --- | --- | --- | --- | --- | --- |
| Options | `CrashCaptureOptions` | `AnsightCrashCaptureOptions` | `AnsightCrashCaptureOptions` | `AnsightCrashCaptureOptions` | `AnsightCrashCaptureOptions` | `AnsightCrashCaptureOptions` |
| Enable | `WithCrashCapture(...)` | `withCrashCapture(...)` | `withCrashCapture(...)` | `withCrashCapture(...)` | `withCrashCapture(...)` | `withCrashCapture(...)` |
| Disable | `WithoutCrashCapture()` | `withoutCrashCapture()` | `withoutCrashCapture()` | `withoutCrashCapture()` | `withoutCrashCapture()` | `withoutCrashCapture()` |
| Framework enrichment | `Runtime.RecordCrashCandidate(...)` and `AppDomain.UnhandledException` | `recordCrashCandidate(...)` | `recordCrashCandidate(...)` | `installErrorHandlers(...)` | `installErrorHandlers(...)` | instrumentation error hooks |

The options independently control Studio handoff and attachment to an active
offline capture, plus pending-report count, retention, breadcrumb count, and
trace-byte limits. `processSessionId` is the correlation key shared by
enrollment, live sessions, offline manifests, and recovered crash reports.

## Host Auto-Probe Parity

Host auto-probe is the SDK-owned runtime connection loop. While active, it
attempts loopback enrollment for host-local runtimes and retries remembered
registrations. It pauses while a live session is connected and waits for the
retry delay before trying again after a session is lost. It is enabled by the
default runtime options and by each all-in-one/developer preset.

| Concept | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Enable | `WithHostAutoProbe(...)` | `hostAutoProbe.enabled` / `withHostAutoProbe(...)` | `hostAutoProbe.enabled` / `withHostAutoProbe(...)` | `hostAutoProbe.enabled` / `withHostAutoProbe(...)` |
| Disable | `WithoutHostAutoProbe()` | `withoutHostAutoProbe()` | `withoutHostAutoProbe()` | `withoutHostAutoProbe()` |
| Initial delay | `InitialDelay`, default `1s` | `initialDelayMilliseconds`, default `1000` | `initialDelayMilliseconds`, default `1000` | `initialDelayMilliseconds`, default `1000` |
| Probe interval | `ProbeInterval`, default `5s` | `probeIntervalMilliseconds`, default `5000` | `probeIntervalMilliseconds`, default `5000` | `probeIntervalMilliseconds`, default `5000` |
| Retry delay after lost session | `ReconnectDelay`, default `10s` | `reconnectDelayMilliseconds`, default `10000` | `reconnectDelayMilliseconds`, default `10000` | `reconnectDelayMilliseconds`, default `10000` |
| Client name | `ClientName` | `clientName` | `clientName` | `clientName` |

## Cellular Host Connection Parity

Cellular host connections are explicitly opt-in and default to disabled in
every SDK and all-in-one/developer preset. The policy is enforced by the shared
connector, so it covers enrollment scans, remembered registrations, and
explicit test connection requests.

| SDK | Builder opt-in | Option |
| --- | --- | --- |
| .NET | `WithCellularHostConnections()` | `AllowCellularConnections` |
| Android | `withCellularHostConnections()` | `allowCellularConnections` |
| iOS | `withCellularHostConnections()` | `allowCellularConnections` |
| Flutter | `withCellularHostConnections()` | `allowCellularConnections` |
| React Native | `withCellularHostConnections()` | `hostConnection.allowCellularConnections` |
| Capacitor | `withCellularHostConnections()` | `hostConnection.allowCellularConnections` |

Opting in can consume mobile data and permits discovery/session connection
attempts over a broader or carrier-managed network. Use the option only with a
trusted Studio host or personal hotspot.

## Unattended Physical-Device Provisioning

Unattended provisioning is explicitly opt-in and disabled by default. It lets
a host test runner inject a fresh, app-specific, one-use enrollment invite at
process launch, after which the native SDK remembers the app-installation
registration in platform-private storage.

| SDK | Builder opt-in | Launch input |
| --- | --- | --- |
| .NET / MAUI | `WithUnattendedProvisioning()` | Native platform input below |
| Android | `withUnattendedProvisioning()` | Activity string extra `ai.ansight.bootstrap.payload` |
| iOS | `withUnattendedProvisioning()` | Process environment variable `ANSIGHT_ENROLLMENT_PAYLOAD` |

Use this only in development or test builds. The launch input is consumed
without logging its bearer payload and is not a substitute for a production
authentication flow.

## Quickstart Equivalents

.NET:

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .Build();

Runtime.InitializeAndActivate(options);
```

Android:

```kotlin
import ai.ansight.Ansight

Ansight.initializeAndActivate(application)
```

iOS:

```swift
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
```

React Native:

```ts
import Ansight from "@ansight/react-native";

await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: __DEV__,
  clientName: "React Native App",
  toolGuard: __DEV__ ? "readOnly" : "disabled",
});

```

Capacitor:

```ts
import Ansight from "@ansight/capacitor";

await Ansight.initializeAndActivate(
  Ansight.createOptionsBuilder()
    .withAnsightDefaults()
    .withReadOnlyToolAccess()
    .withDomTools()
    .withErrorCapture()
    .build(),
);

```

The Capacitor facade intentionally follows the React Native camel-case runtime
API shown in the maps below, while adding `dom.*` WebView tools and a fluent
options builder.

Flutter:

```dart
import 'package:ansight_flutter/ansight.dart';
import 'package:flutter/widgets.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Ansight.instance.initializeAndActivate(
    AnsightOptions.developer(
      clientName: 'Flutter App',
      toolGuard: AnsightToolGuard.readOnly,
    ),
  );
  await AnsightFlutterInstrumentation.instance.install();
  await Ansight.instance.enrollFromQrCode(clientName: 'Flutter App');
  runApp(const App());
}
```

The `ansight_flutter` package bridges the Android and iOS runtimes and adds
Flutter lifecycle, frame, error, navigation, Dart tool/artifact, and widget
inspection support. Its `Ansight.instance` methods follow the same concepts in
the maps below with idiomatic Dart naming.

## Runtime API Map

| Concept | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Initialize | `Runtime.Initialize(options)` | `AnsightRuntime.initialize(application, options)` | `AnsightRuntime.shared.initialize(options:)` | `Ansight.initialize(options)` |
| Initialize and activate | `Runtime.InitializeAndActivate(options)` | `Ansight.initializeAndActivate(application, options)` | `initializeAndActivate(options:)` | `Ansight.initializeAndActivate(options)` |
| Activate | `Runtime.Activate()` | `AnsightRuntime.activate()` | `activate()` | `Ansight.activate()` |
| Deactivate | `Runtime.Deactivate()` | `AnsightRuntime.deactivate()` | `deactivate()` | `Ansight.deactivate()` |
| Clear | `Runtime.Clear()` | `AnsightRuntime.clear()` | `clear()` | `Ansight.clear()` |
| Metric | `Runtime.Metric(value, channel)` | `AnsightRuntime.metric(value, channel)` | `metric(_:channel:)` | `Ansight.metric(value, channel)` |
| Event | `Runtime.Event(...)` | `AnsightRuntime.event(...)` | `event(...)` | `Ansight.event(...)` |
| Screen viewed | `Runtime.ScreenViewed(...)` | `screenViewed(...)` | `screenViewed(...)` | `screenViewed(...)` or `trackRoute(...)` |
| Lifecycle | `SetAppLifecycleState(...)` | `setAppLifecycleState(...)` | `setAppLifecycleState(...)` | `setAppLifecycleState(...)` |
| Custom log | `Runtime.SendClientLogAsync(...)` | `sendClientLog(line)` | `sendClientLog(_:)` | `sendClientLog(line)` |
| Status | `Runtime.HostConnection.Status` | `hostConnectionStatus()` | `hostConnectionStatus()` | `hostConnectionStatus()` |
| Capabilities | `Runtime.HostConnection.Capabilities` | `hostConnectionCapabilities()` | `hostConnectionCapabilities()` | `hostConnectionCapabilities()` |
| Config changed | `NotifyConfigChangedAsync()` | `notifyHostConnectionConfigChanged()` | `notifyHostConnectionConfigChanged()` | `notifyHostConnectionConfigChanged()` |
| Status listener | `Runtime.HostConnection.StatusChanged` | `addHostConnectionStatusListener(...)` | `addHostConnectionStatusListener(...)` | `addHostConnectionStatusListener(...)` |
| Snapshot | `Runtime.Instance.DataSink` / debug state | `snapshot()` | `snapshot()` | `snapshot()` |
| Current options | Options instance | `options()` | `currentOptions()` | `currentOptions()` |
| FPS toggle/status | `IsFramesPerSecondEnabled`, `EnableFramesPerSecond()`, `DisableFramesPerSecond()` | `isFramesPerSecondEnabled()`, `enableFramesPerSecond()`, `disableFramesPerSecond()` | `isFramesPerSecondEnabled`, `enableFramesPerSecond()`, `disableFramesPerSecond()` | `isFramesPerSecondEnabled()`, `enableFramesPerSecond()`, `disableFramesPerSecond()` |
| Touch capture guard/status | `IsTouchCaptureEnabled`, `SetTouchCaptureGuard(...)` | `isTouchCaptureEnabled()`, `setTouchCaptureGuard(...)` | `isTouchCaptureEnabled`, `setTouchCaptureGuard(...)` | Native bridge exposes enable/disable; JS guard is app-owned |
| SDK log hook | `Logger` callback APIs | `AnsightLogger.registerCallback(...)` | `AnsightLogger.registerCallback(...)` | `addLogListener(...)` |
| Retained metrics/events | `Runtime.Instance.DataSink` | `recordedMetrics()`, `recordedEvents()` | `recordedMetrics()`, `recordedEvents()` | `recordedMetrics()`, `recordedEvents()` |
| Custom tool registration | `AddTool(...)` | `registerTool(...)` | `registerTool(...)` | `registerTool(...)` |
| Artifact providers | `AddArtifactProvider(...)` | `addArtifactProvider(...)` | `registerArtifactProvider(...)` | `registerArtifactProvider(...)` |
| Manual frame capture | On-demand `ui.get_screenshot` | `captureScreenFrame(...)` | `captureScreenFrame(...)` | `captureScreenFrame(...)` |
| Sampled app metric stream | Record from app sampler with `Runtime.Metric(...)` | `registerMetricStream(...)` | `registerMetricStream(...)` | No JavaScript sampler API |

## Option Map

| Concept | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Sample interval | `WithSampleFrequencyMilliseconds(...)` | `AnsightOptions.sampleFrequencyMilliseconds` | `AnsightOptions.sampleFrequencyMilliseconds` | `sampleFrequencyMilliseconds` |
| Retention | `WithRetentionPeriodSeconds(...)` | `retentionPeriodSeconds` | `retentionPeriodSeconds` | `retentionPeriodSeconds` |
| FPS | `WithFramesPerSecond()` | `enableFramesPerSecond` | `enableFramesPerSecond` | `enableFramesPerSecond` |
| Battery | `WithBatteryLevel()` | `enableBatteryLevel` | `enableBatteryLevel` | `enableBatteryLevel` |
| Memory channels | `DefaultMemoryChannels` | `defaultMemoryChannels` | `defaultMemoryChannels` | `defaultMemoryChannels` |
| Custom channels | `AddAdditionalChannel(...)` | `additionalChannels` | `additionalChannels` | `additionalChannels` |
| JPEG capture | `WithSessionJpegCapture(...)` / `SessionJpegCaptureOptions.CaptureGpuBackedSurfaces` | `sessionJpegCapture` with `captureGpuBackedSurfaces` parity flag | `sessionJpegCapture.captureGpuBackedSurfaces` | `sessionJpegCapture.captureGpuBackedSurfaces` |
| Touch capture | `WithTouchCapture(...)` | `touchCapture` | `touchCapture` | `touchCapture` |
| Tool guard | `WithReadOnlyToolAccess()` etc. | `toolGuard` | `toolGuard` | `toolGuard` |
| Custom properties | `RegisterCustomProperty(...)` / runtime mutations | `customProperties` | `customProperties` | `customProperties` |
| Host auto-probe | `WithHostAutoProbe(...)` | `hostAutoProbe` | `hostAutoProbe` | `hostAutoProbe` |
| Host connection | `ConfigureHostConnection(...)` | `hostConnection` | `hostConnection` | `hostConnection` |
| Native tool suites | `With...Tools(...)` options | `with...Tools(...)` options | `AnsightRemoteToolOptions` | `remoteTools` |
| Artifact providers | `AddArtifactProvider(...)` | `artifactProviders` / builder methods | `AnsightRemoteToolOptions.artifactProviders` or runtime registration | Runtime `registerArtifactProvider(...)` |

## Host Connection

Host-local runtimes register automatically through loopback while
`ansight host run` is active; no account is required. For a physical device,
run `ansight pairing issue --qr`, then exchange that one-use invite for an
app-installation registration. Later launches reconnect from app-private state.

| Concept | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Auto connect | `HostConnectionRequest.Auto()` | `HostConnectionRequest()` or `Auto` | `.auto(...)` | `connect(null, options)` |
| QR enrollment | `HostConnectionRequest.QrCode()` | `Ansight.enrollFromQrCode(...)` | `.qrCode(...)` | `enrollFromQrCode(...)` |
| Payload connect | `PayloadText(json)` | `HostConnectionRequest.payload(json)` | `.payloadText(json, ...)` | `connect(json, options)` |
| Clear registration | `ClearCachedSessionAsync()` | `clearCachedSession()` | `clearCachedSession()` | `clearCachedSession()` |
| Expected app id | Request option | `expectedAppId` on save/open paths | request option | `expectedAppId` |
| Host override | Request option | `hostAddressOverride` | request option | `hostAddressOverride` |
| Simulator host fallback | yes | Android emulator host address | iOS Simulator localhost | inherited from native runtime |

Keep enrollment UI and remote tools in local-development builds. The flow has
no build-time generation, embedded payload, signing key, certificate, or
manually configured host address.

## Screenshot Capture Ownership

Current .NET, Android, and iOS runtimes add
`sessionJpegCaptureControlVersion: 1` to `device.profile`. Studio may acknowledge
that profile with `sessionJpegCapture.mode: "host"` and an optional source such
as `adb` or `simctl`. In host mode, the SDK suspends its periodic in-app JPEG
loop for that live session. Missing or app mode keeps the configured SDK
capture behavior. React Native, Capacitor, and Flutter inherit this negotiation
from their native runtime.

## Session Visual-Tree Capture Modes

The `sessionJpegCapture.mode` setting is available on .NET, Android, iOS, React
Native, Capacitor, and Flutter:

| Wire value | Native enum | Behavior |
| --- | --- | --- |
| `screenshotOnly` | `ScreenshotOnly` / `screenshotOnly` | Sends periodic screenshots without automatic visual trees. |
| `screenshotAndVisualTree` | `ScreenshotAndVisualTree` / `screenshotAndVisualTree` | Captures a visual tree for each SDK screenshot and correlates it through `screenshotCapturedAtUtc`. |
| `screenshotWithVisualTreeOnTouch` | `ScreenshotWithVisualTreeOnTouch` / `screenshotWithVisualTreeOnTouch` | Keeps screenshots on their configured schedule and captures best-effort visual trees on touch down and touch up; rapid boundaries are coalesced through a bounded latest-value queue. |

Touch-triggered capture requires `touchCapture` to be configured and runtime
touch capture to be enabled. It continues to work when Studio owns simulator
or emulator screenshots because the visual-tree timeline is independent of
the JPEG producer. Each emitted `CLIENT_VISUAL_TREE` has source
`sdk.touchCapture`, a gesture id and phase, the triggering touch action and
timestamp, and no fabricated `screenshotCapturedAtUtc` correlation.
Tree captures are rate-limited to at most the screenshot cadence, with a
750 ms minimum interval, so repeated gestures cannot create an unbounded UI
capture backlog.

## Tool Guards

| Guard | Discovery | Execution | Allowed scopes |
| --- | --- | --- | --- |
| Disabled | No | No | None |
| ReadOnly | Yes | Yes | Read |
| ReadWrite | Yes | Yes | Read, Write |
| FullAccess | Yes | Yes | Read, Write, Delete |

Naming:

- .NET builder methods: `WithToolsDisabled()`, `WithReadOnlyToolAccess()`,
  `WithReadWriteToolAccess()`, `WithAllToolAccess()`, `WithToolGuard(...)`.
- Android enum: `AnsightToolGuard.Disabled`, `ReadOnly`, `ReadWrite`,
  `FullAccess`.
- iOS presets: `.disabled`, `.readOnly`, `.readWrite`, `.fullAccess`.
- React Native strings: `"disabled"`, `"readOnly"`, `"readWrite"`,
  `"fullAccess"`. `"full"` is accepted as a compatibility alias.

Delete-scoped tools, such as `files.delete_file`, `prefs.remove_key`,
`secure.remove_key`, and overlay removal tools, require `FullAccess`.

## Tool Suites

The protocol tool ids are shared across implementations. Package availability
still differs: file-descriptor diagnostics are currently implemented only by
the Android and iOS runtimes, while MAUI and React tool ids are
framework-specific.

| Suite | Tool ids |
| --- | --- |
| App artifacts | `artifacts.query`, `artifacts.request` |
| Visual tree | `ui.get_visual_tree`, `ui.get_screenshot`, `ui.inspect_node`, `ui.show_overlay`, `ui.get_overlay`, `ui.query_overlays`, `ui.update_overlay`, `ui.remove_overlay`, `ui.clear_overlays` |
| Files | `files.list_directory`, `files.read_file`, `files.get_file_checksum`, `files.download_file`, `files.begin_binary_download`, `files.push_file`, `files.copy_file`, `files.move_file`, `files.delete_file` |
| File descriptor diagnostics | `file_descriptors.list_open`, `file_descriptors.count_open`, `file_descriptors.inspect`, `file_descriptors.get_usage` |
| JNI reference diagnostics | `jni_references.capture_graph` |
| Preferences | `prefs.list_keys`, `prefs.get_value`, `prefs.set_value`, `prefs.remove_key` |
| Secure storage | `secure.get_value`, `secure.set_value`, `secure.remove_key` |
| Database | `data.list_databases`, `data.describe_schema`, `data.query` |
| Reflection | `reflect.list_roots`, `reflect.inspect_object`, `reflect.describe_type`, `reflect.set_member_value`, `reflect.invoke_method` |
| .NET MAUI | `maui.get_current_page`, `maui.get_visual_tree`, `maui.find_elements`, `maui.get_element`, `maui.get_bindable_property`, `maui.set_bindable_property`, `maui.clear_bindable_property`, `maui.inflate_xaml`, `maui.add_element`, `maui.remove_element`, `maui.set_app_theme`, `maui.get_binding_context`, `maui.get_bindings`, `maui.get_resource_state`, `maui.get_navigation_state`, `maui.invoke_element_action`, `maui.wait_for_ui`, `maui.get_layout_diagnostics`, `maui.get_handler_diagnostics`, `maui.invoke_binding_context_command`, `maui.set_binding_context_property` |
| React Native | `react.get_component_tree`, `react.get_shadow_tree`, `react.find_components`, `react.get_component`, `react.get_navigation_state`, `react.invoke_component_action` |

`reflect.list_roots` includes a `hostRuntime` descriptor for every root so
Studio and agent bridges can distinguish which runtime owns the object graph.
Current SDK roots report `kind: "dotnet"` for CLR roots, `kind: "jvm"` for
Android JVM/ART roots, and `kind: "swift"` for iOS Swift/Objective-C roots.
Future React Native JavaScript reflection roots can use the same field with
`kind: "javascript"` and `bridge: "react-native"`.

Swift reflection inspection uses `Mirror`. Unlike .NET and Android/JVM,
arbitrary Swift property writes and method invocation are not available through
runtime reflection; iOS roots opt in to `reflect.set_member_value` and
`reflect.invoke_method` by conforming to `AnsightReflectionMutableRoot` and
`AnsightReflectionInvokableRoot`.

Each SDK emits the same `tool.query`, `tool.catalog`, `tool.call`,
`tool.result`, and `tool.error` protocol shapes described in
[Protocol](protocol.md#remote-tool-protocol).

Tool-suite registration follows the same app-code convention:

| Suite | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| App artifacts | `AddArtifactProvider(...)` | `addArtifactProvider(...)` | `registerArtifactProvider(...)` | `registerArtifactProvider(...)` |
| All current native tools | `WithAnsightRemoteTools()` | `withAnsightRemoteTools()` | `registerAnsightRemoteTools(options:)` | `remoteTools` |
| Developer defaults and tools | `WithAnsightSdk(...)` | `withAnsightSdk { ... }` or `Ansight.developerOptions(...)` | `initializeAndActivateAnsightSdk(...)` | `withAnsightSdk(...)` |
| Visual tree | `WithVisualTreeTools()` | `withVisualTreeTools()` | `registerVisualTreeTools()` | native default plus `installReactTools(...)` |
| Files | `WithFileSystemTools(...)` | `withFileSystemTools { addRoot(...) }` | `AnsightFileSystemToolsOptions.createBuilder()` | `remoteTools.fileSystem` |
| File descriptor diagnostics | — | `withFileDescriptorDiagnosticsTools { includeTargets(...) }` | `registerFileDescriptorDiagnosticsTools(options:)` | Native bridge |
| JNI reference diagnostics | `WithJniReferenceDiagnosticsTools()` (Android) | `withJniReferenceDiagnosticsTools { maximumGraphNodes(...) }` | — | Android native bridge |
| Preferences | `WithPreferencesTools(...)` | `withPreferencesTools { allowKey(...) }` | `AnsightPreferencesToolOptions.createBuilder()` | `remoteTools.preferences` |
| Secure storage | `WithSecureStorageTools(...)` | `withSecureStorageTools { allowKey(...) }` | `AnsightSecureStorageToolsOptions.createBuilder()` | `remoteTools.secureStorage` |
| Database | `WithDatabaseTools()` | `withDatabaseTools { includePlatformRoots(...) }` | `AnsightDatabaseToolsOptions.createBuilder()` | `remoteTools.database` |
| Reflection | `WithReflectionTools(...)` | `withReflectionTools { allowRoot(...) }` | `AnsightReflectionToolsOptions.createBuilder()` | `remoteTools.reflection` |
| MAUI | `WithMauiTools()` | — | — | — |
| React | — | — | — | `installReactTools(...)` |

## App Artifacts

Artifact providers are core SDK extensibility, not a separate tool package. A
provider advertises dynamically available exports and creates a requested
snapshot. Registering the first provider installs the read-scoped
`artifacts.query` and `artifacts.request` tools.

| Concept | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Provider contract | `IArtifactProvider` | `AndroidArtifactProvider` | `AnsightArtifactProvider` | `AnsightArtifactProvider` |
| Register at setup | `AddArtifactProvider(...)` | `addArtifactProvider(...)` | `AnsightRemoteToolOptions.artifactProviders` | `registerArtifactProvider(...)` |
| Register at runtime | Options-owned registry | Reinitialize with options | `registerArtifactProvider(...)` | `registerArtifactProvider(...)` |
| Text/bytes | `ArtifactPayload.FromText/FromBytes` | `ByteArray` result | `AnsightArtifactPayload.fromText/fromBytes` | string, bytes, `ArrayBuffer`, or `Uint8Array` |
| Stream/file source | `FromStream/FromFile` | Provider returns bytes | Provider returns payload data | Provider returns JS payload data |
| Transfer | Live `ASFT` binary frames | Live native binary frames | Live native binary frames | JS provider plus native binary frames |

Artifact requests require a live tool request and pairing transport. Provider
query failures are isolated in the catalog; request or transfer failures return
the `artifact_*` error codes documented in
[Protocol](protocol.md#artifact-tools).

## Framework-Specific Workflows

| Workflow | Availability | Entry point |
| --- | --- | --- |
| MAUI initialization plus automatic lifecycle/page views | .NET MAUI | `UseAnsight<App>()` |
| MAUI inspection and mutation | .NET MAUI | `WithMauiTools()` |
| Annotated feedback overlay, evidence hooks, bundles, live/offline sinks | .NET Android/iOS/Mac Catalyst Debug app builds | `WithAnnotatedFeedback()` / `Feedback.PresentAsync()` |
| Offline capture, retention, ZIP/AES export, and team upload | .NET | `OfflineCapture.Configure(...)` |
| Objective-C runtime facade | iOS | `ANSAnsight` |
| React component/shadow tree and actions | React Native | `installReactTools(...)` |
| React Navigation screen tracking | React Native | `createReactNavigationTracker(...)` |
| JavaScript unhandled-error capture | React Native | `installErrorHandlers(...)` |
| Flutter widget-tree inspection and actions | Flutter | `AnsightFlutterInstrumentation.instance.install()` |
| Flutter route and screen-view tracking | Flutter | `AnsightNavigatorObserver` |
| Flutter errors, frame timing, and lifecycle capture | Flutter | `AnsightFlutterInstrumentation.instance.install()` |

`Ansight` and `Ansight.Maui` reference the annotations and offline-capture
packages, but do not automatically start either workflow. Annotated feedback
must be explicitly enabled and remains disabled in Release application builds.

## Custom Tools

Custom tools must declare an id, name, category, scope, optional schemas, and a
handler. Tool ids are unique; Android and iOS support replacing an existing id
when the bridge needs to refresh a JavaScript-backed handler.

Android:

```kotlin
AnsightRuntime.registerTool(
    FunctionAndroidTool(
        definition = ToolDefinition(
            id = "app.state.snapshot",
            name = "State Snapshot",
            description = "Returns current app state.",
            category = "app",
            scope = ToolScope.Read,
            keywords = "state snapshot",
        ),
    ) { _, _ ->
        AndroidToolResult.success(JSONObject().put("state", "ready"))
    },
)
```

iOS:

```swift
struct StateSnapshotTool: AnsightTool {
    var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: "app.state.snapshot",
            name: "State Snapshot",
            category: "app",
            scope: .read
        )
    }

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        .success(.object(["state": .string("ready")]))
    }
}

try AnsightRuntime.shared.registerTool(StateSnapshotTool())
```

React Native:

```ts
const registration = Ansight.registerTool(
  {
    id: "app.state.snapshot",
    name: "State Snapshot",
    category: "app",
    scope: "Read",
  },
  async () => ({ success: true, result: { state: "ready" } }),
);

await registration.ready;
```

Flutter:

```dart
final registration = await Ansight.instance.registerTool(
  const AnsightToolDefinition(
    id: 'app.state.snapshot',
    name: 'State Snapshot',
    category: 'app',
    scope: AnsightToolScope.read,
  ),
  (arguments, context) async => const AnsightToolResult.success(
    result: <String, Object?>{'state': 'ready'},
  ),
);
```

## Client Logs

`sendClientLog` sends a line of app-provided text over the active live session.
It is intended for explicit app/debug messages, not automatic platform logcat
or OSLog mirroring. When no live session is connected, the method returns an
operation result that describes the unavailable transport.

React Native example:

```ts
await Ansight.sendClientLog("Checkout loaded cartId=debug-42");
```

Android example:

```kotlin
AnsightRuntime.sendClientLog("Checkout loaded cartId=debug-42")
```

iOS example:

```swift
await AnsightRuntime.shared.sendClientLog("Checkout loaded cartId=debug-42")
```

Flutter example:

```dart
await Ansight.instance.sendClientLog('Checkout loaded cartId=debug-42');
```

## Validation Commands

Run these checks after changing SDK API docs or examples:

```bash
cd /Users/matthewrobbins/Development/git/ansight-sdk/src/android
./gradlew :ansight-core:test :ansight:test :harness:assembleDebug

cd /Users/matthewrobbins/Development/git/ansight-sdk/src/ios
swift test

cd /Users/matthewrobbins/Development/git/ansight-sdk/src/react-native
npm run check

cd /Users/matthewrobbins/Development/git/ansight-sdk/src/flutter
flutter analyze
flutter test
dart pub publish --dry-run
```

For broader corpus validation, use:

```bash
python /Users/matthewrobbins/Development/git/ansight-sdk/scripts/validate_android_test_apps.py --help
python /Users/matthewrobbins/Development/git/ansight-sdk/scripts/validate_ios_test_apps.py --help
dart run /Users/matthewrobbins/Development/git/ansight-sdk/src/flutter/tool/flutter_corpus.dart --help
```
