# Ansight iOS

The native harness app lives in `Examples/NativeHarness/`.

Import `AnsightCore` plus individual enrollment/tool products for a minimal
integration, or import the aggregate `Ansight` product for developer defaults,
QR enrollment, and the current native remote-tool suites.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Install

Add the package with SwiftPM and select the aggregate `Ansight` product for the
first integration:

```swift
.package(
    url: "https://github.com/ansight-ai/ansight-sdk.git",
    exact: "1.3.0-preview.11"
)
```

The matching CocoaPod is also published:

```ruby
pod 'Ansight', '1.3.0-preview.11'
```

Use `AnsightCore` plus selected tool products or pods only when the app needs a
narrower surface. See the
[iOS getting-started guide](https://www.ansight.ai/docs/sdk/ios/setup) for the
complete package matrix, guarded startup locations, and CLI verification.

## Current capabilities

- runtime initialization, activation, deactivation, clearing, manual and automatic UIKit lifecycle state, screen views, custom metrics, custom events, and structured debug snapshots
- shared telemetry bounds: 200-2000ms sampling, 60-3600s retention, reserved channel validation, bounded retained metric/event buffers
- one-use `ansight.enrollment-invite.v2` parsing with saved app-installation registration
- discovery-hint host resolution for local developer flows
- host connection status/results for automatic reconnect, QR enrollment, and app-provided scanner payloads
- correlated `ENROLLMENT_CONNECT`/`ENROLLMENT_RESULT` UDP bootstrap and clear-text WebSocket handoff
- live control messages for `session.open`, `device.profile`, and `app.state`
- live metric channel, metric sample, and app event streaming using the established `CLIENT_METRIC_CHANNELS`, `CLIENT_METRICS`, and `CLIENT_EVENTS` payloads
- automatic UIKit foreground/background, UIKit view-controller screen-view, and SwiftUI `UIHostingController` root-view capture with explicit opt-out controls and app-provided route naming hooks
- FPS telemetry sampling through `CADisplayLink` on UIKit platforms using the reserved FPS metric channel
- live JPEG screen-frame capture using Studio's binary `ASJP` / `CLIENT_JPEG` WebSocket path
- live UIKit touch capture using a simultaneous window gesture recognizer and Studio-compatible `CLIENT_TOUCH_INPUT` / `ansight.touches.v1` packed batches
- baseline Apple device/app profile collection without direct PII, including runtime stack codes, app icon payloads, Metal GPU/render-backend details, and coarse network transport
- Keychain-backed app-installation registration and remembered host profiles with explicit clearing
- queued `ansight.file-transfer.v1` binary artifact transfers for screenshot and file-download tools during live Studio sessions
- executable tool registration, tool guard policy, tool security metadata, reserved tool call context arguments, and `tool.query` / `tool.call` protocol handling
- app artifact providers with `artifacts.query`, `artifacts.request`, and live binary export
- `AnsightToolsPreferences` SwiftPM product with `prefs.list_keys`, `prefs.get_value`, `prefs.set_value`, and `prefs.remove_key` for sandboxed `UserDefaults` access
- `AnsightToolsFileSystem` SwiftPM product with sandboxed directory listing, file read/checksum/download, live binary download, push/copy/move/delete tools
- `AnsightToolsFileDescriptorDiagnostics` SwiftPM product with open descriptor listing, counting, inspection, and usage/limit diagnostics
- `AnsightToolsDatabase` SwiftPM product with SQLite discovery, schema inspection, and constrained read-only query tools: `data.list_databases`, `data.describe_schema`, and `data.query`
- `AnsightToolsSecureStorage` SwiftPM product with allow-listed Keychain `secure.get_value`, `secure.set_value`, and `secure.remove_key` tools
- `AnsightToolsReflection` SwiftPM product with registered-root `reflect.*` inspection tools and opt-in write/invoke hooks
- `AnsightToolsVisualTree` SwiftPM product with UIKit visual tree, node inspection, live screenshot, and diagnostic overlay tools
- `AnsightPairingQR` SwiftPM product with AVFoundation QR scanning for SDK-owned enrollment UI
- SwiftPM bundled-tool enforcement

## SwiftPM developer mode

When building this package through SwiftPM, the `AnsightBuildToolPlugin` runs
automatically for the `AnsightCore` target. Set
`ANSIGHT_ALLOW_REMOTE_TOOLS=true` only for targets that intentionally bundle
concrete `AnsightTool` implementations. Enrollment has no build-time host probe
or generated resource.

## CocoaPods package model

The aggregate `Ansight` pod depends on `AnsightCore`, `AnsightPairingQR`,
`AnsightToolsDatabase`, `AnsightToolsFileDescriptorDiagnostics`,
`AnsightToolsFileSystem`, `AnsightToolsPreferences`, `AnsightToolsReflection`,
`AnsightToolsSecureStorage`, and `AnsightToolsVisualTree`. Each component is
also published as an individual pod for minimal integrations.

The `AnsightCore` pod runs the same remote-tool build enforcement before
compile and honors `ANSIGHT_ALLOW_REMOTE_TOOLS`.

## Quickstart

Use the aggregate product for the developer preset and all current native tool
suites:

```swift
#if DEBUG
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
#endif
```

Start the local host in one terminal and leave it running:

```sh
ansight host run
```

Launch the app in iOS Simulator or Mac Catalyst, then verify the connected
session and tool catalog from another terminal:

```sh
ansight session list --connected --json
ansight app tools <session-id> --json
```

The iOS Simulator and Mac Catalyst register automatically through loopback. No
account, pairing file, build environment variable, host address, or build-time
host process is required. If the host is unavailable, the SDK keeps retrying
without affecting the app.

On a physical iPhone, run `ansight pairing issue --qr`, then call
`.qrCode(...)` once from a developer-only app surface. The host registers the
runtime bundle id automatically; no prior app entry or app-specific invite is
required. The scanner requires `NSCameraUsageDescription`, and direct host
access uses Apple's Local Network privacy control. Later launches reconnect
from app-private registration state. Ansight does not add Bluetooth, location,
Bonjour discovery, contacts, photos, or associated-domains access.

The aggregate preset mirrors the .NET all-in-one defaults:

- 400 ms sampling
- 120 second retention
- FPS enabled
- battery disabled
- UIKit lifecycle capture enabled
- JPEG capture every 2000 ms at quality 60, max width 480, and GPU-backed surface capture enabled
- touch capture enabled
- host auto-probe enabled
- full tool access
- current native tool suites registered

Use `AnsightCore` directly for a smaller integration:

```swift
import AnsightCore

let options = try AnsightOptions(
    sampleFrequencyMilliseconds: 500,
    retentionPeriodSeconds: 600,
    toolGuard: .readOnly
).validated()

try AnsightRuntime.shared.initializeAndActivate(options: options)
```

Native and custom HTTP integrations can submit a typed metadata record with
`await AnsightRuntime.shared.recordNetworkRequest(AnsightNetworkRequest(...))`.
V1 has no body fields. `AnsightNetworkRequestSanitizer` is always applied inside
the native runtime immediately before transport, including records received
from React Native, Capacitor, Flutter, or .NET bridges.

## Options

`AnsightOptions` is the Swift equivalent of .NET `Options`.

| Option | Purpose |
| --- | --- |
| `sampleFrequencyMilliseconds` | Built-in telemetry sampling interval. Clamped to 200-2000 ms. |
| `retentionPeriodSeconds` | Local metric/event retention window. Clamped to 60-3600 seconds. |
| `additionalChannels` | Registers custom metric channels. Reserved ids are rejected. |
| `defaultMemoryChannels` | Selects managed heap, native heap, RSS, and physical footprint channels. |
| `enableFramesPerSecond` | Enables CADisplayLink FPS sampling. |
| `enableBatteryLevel` | Enables battery sampling where available. |
| `enableOpenFileHandleTracking` | Enables process open-file-handle sampling. Disabled by default. |
| `lifecycleCapture` | Controls automatic UIKit app lifecycle and screen-view capture. |
| `sessionJpegCapture` | Configures live JPEG screen-frame streaming and automatic visual-tree mode. `nil` disables it. `captureGpuBackedSurfaces` defaults to `true` so Metal, SceneKit, and similar GPU-backed views are included. |
| `touchCapture` | Configures app-local touch capture. `nil` disables it. |
| `toolGuard` | Controls remote-tool discovery and execution. |
| `customProperties` | Grouped string properties sent with `session.open`. |
| `hostAutoProbe` | Controls remembered-host retries after the host disappears and later reappears. |
| `hostConnection` | Configures registration retention, discovery, and network policy. |

Use `withOpenFileHandleTracking()` to expose and sample `Open File Handles` on
reserved channel 7. Tracking is disabled by default and can be turned off again
with `withoutOpenFileHandleTracking()`.

Cellular host connections are disabled by default. Enrollment and reconnect
requests use the same restriction. Opt in only for a trusted development host
or personal hotspot:

```swift
let options = try AnsightOptions.createBuilder()
    .withCellularHostConnections()
    .build()
```

The underlying `allowCellularConnections` option can consume mobile data and
allows connection attempts over a broader or carrier-managed network. Use it
only with a trusted development host.

## Host Connection

Runtime-owned host connection APIs live on `AnsightRuntime.shared`.
For a physical device, first run `ansight pairing issue --qr` against the local
host, then open the scanner from a developer-only surface:

```swift
let result = await AnsightRuntime.shared.connect(
    .qrCode(title: "Scan Ansight Enrollment QR")
)
```

This is the physical-device first-use flow. After a successful scan,
`.auto(...)` reconnects with the stored registration.

For unattended physical-device test runs, explicitly enable launch-time
provisioning in the test build:

```swift
let options = try AnsightOptions.createBuilder(.ansightDeveloperDefaults)
    .withUnattendedProvisioning()
    .build()

try AnsightRuntime.shared.initializeAndActivateAnsightSdk(options: options)
```

When `.auto(...)` runs, the SDK checks the
`ANSIGHT_ENROLLMENT_PAYLOAD` process environment variable before its remembered
profiles. A successful connection saves the registration in Keychain and
clears the process environment value. The option is disabled by default and
should be enabled only in development or test builds. A host runner can launch
a signed app with `xcrun devicectl device process launch --environment-variables`
to supply a fresh one-use enrollment payload without user input.

For iOS Simulator and Mac Catalyst, activation automatically performs local
enrollment and connection; no explicit `connect` call is required.

Host auto-probe is enabled by default while the runtime is active. It remembers
previous host connections and retries them so the app can reconnect after the
host disappears and later reappears. Probing pauses while a live session is
connected and resumes after the retry delay when that session is lost:

```swift
let options = try AnsightOptions.createBuilder()
    .withHostAutoProbe(
        AnsightHostAutoProbeOptions(
            enabled: true,
            initialDelayMilliseconds: 1_000,
            probeIntervalMilliseconds: 5_000,
            reconnectDelayMilliseconds: 10_000,
            clientName: "iOS App"
        )
    )
    .build()
```

Use `withoutHostAutoProbe()` for flows where reconnects should only happen
after an explicit app action.

If the app already owns a scanner, pass its result through the explicit payload
API:

```swift
await AnsightRuntime.shared.connect(
    .payloadText(
        enrollmentPayload,
        clientName: "iOS App",
        expectedAppId: Bundle.main.bundleIdentifier
    )
)

AnsightRuntime.shared.clearCachedSession()
await AnsightRuntime.shared.disconnect()
```

`HostConnectionResult` reports `success`, `message`, request kind, source,
optional reason code, and live-session details when a session opens.

## Telemetry, Logs, And Properties

Record app telemetry:

```swift
try AnsightRuntime.shared.registerMetricChannel(
    AnsightChannel(id: 42, name: "Cache", color: "#FF9500", unit: "items", type: "cache")
)

try AnsightRuntime.shared.metric(12, channel: 42)
try AnsightRuntime.shared.event("cache_hit", type: .info, details: "warm=true", channel: 42)
try AnsightRuntime.shared.screenViewed("Orders", details: ["route": "/orders"])
AnsightRuntime.shared.setAppLifecycleState(.foreground)
```

Register sampled values with `AnsightMetricStream`:

```swift
try AnsightRuntime.shared.registerMetricStream(
    AnsightMetricStream(
        channel: AnsightChannel(id: 43, name: "Queue Depth", unit: "items", type: "queue")
    ) {
        Int64(queue.depth)
    }
)
```

Send a custom client log over the live session:

```swift
await AnsightRuntime.shared.sendClientLog("Checkout loaded cartId=debug-42")
```

Observe SDK-internal logs with `AnsightLogger`:

```swift
let callback = AnsightClosureLogCallback { level, message, error in
    print("[Ansight] \(level.rawValue): \(message)")
}

AnsightLogger.registerCallback(callback)
AnsightLogger.removeCallback(callback)
```

Update grouped session properties:

```swift
await AnsightRuntime.shared.updateSessionProperties([
    "app": [
        "region": "au",
        "tenant": "debug"
    ]
])

await AnsightRuntime.shared.clearSessionProperties()
```

## Tool Guards

| Preset | Allowed scopes |
| --- | --- |
| `.disabled` | None |
| `.readOnly` | `.read` |
| `.readWrite` | `.read`, `.write` |
| `.fullAccess` | `.read`, `.write`, `.delete` |

Delete-scoped tools, such as `files.delete_file`, `prefs.remove_key`,
`secure.remove_key`, and overlay removal tools, require `.fullAccess`.

## Runtime Toggles

FPS sampling can be changed after initialization:

```swift
if !AnsightRuntime.shared.isFramesPerSecondEnabled {
    AnsightRuntime.shared.enableFramesPerSecond()
}

AnsightRuntime.shared.disableFramesPerSecond()
```

Touch capture can be guarded by app state:

```swift
AnsightRuntime.shared.setTouchCaptureGuard {
    sessionManager.isDebugSessionAllowed
}
```

## Screen capture

> **Important:** Screen capture will result in an FPS drop while the SDK renders,
> encodes, and sends frames. Use conservative interval, quality, and max-width
> settings, and disable `sessionJpegCapture` for performance-focused runs unless
> visual evidence is required.

Configure `AnsightOptions.sessionJpegCapture` before connecting to Studio:

```swift
try AnsightRuntime.shared.initializeAndActivate(
    options: AnsightOptions(
        sessionJpegCapture: AnsightSessionJpegCaptureOptions(
            intervalMilliseconds: 2_000,
            quality: 60,
            maxWidth: 480,
            captureGpuBackedSurfaces: true
        )
    )
)
await AnsightRuntime.shared.connect(.auto(clientName: "iOS Native Harness"))
```

`captureGpuBackedSurfaces` defaults to `true` so Metal, SceneKit, and similar
GPU-backed views are included. Set it to `false` to use a lower-overhead capture
path when those surfaces are not needed.

## Tool Suite Options

The aggregate product accepts one `AnsightRemoteToolOptions` value for native
tool-suite configuration:

```swift
let fileSystem = AnsightFileSystemToolsOptions.createBuilder()
    .addRoot(alias: "exports", path: exportsDirectory.path)
    .build()

let reflection = AnsightReflectionToolsOptions.createBuilder()
    .includeBuiltInRoots(false)
    .allowRoot("debug.model")
    .allowTypePrefix("MyApp.")
    .build()

try AnsightRuntime.shared.initializeAndActivateAnsightSdk(
    remoteToolOptions: AnsightRemoteToolOptions(
        fileSystem: fileSystem,
        reflection: reflection
    )
)
```

When the WebSocket session opens, the SDK captures the foreground UIKit window on the main actor, encodes it as JPEG, and sends binary frames to Studio. Apps can also trigger a single frame with `await AnsightRuntime.shared.captureScreenFrame()`.

Set `mode: .screenshotWithVisualTreeOnTouch` to retain periodic screenshots
while capturing visual trees only on touch down and touch up. Move and cancel
events do not trigger capture. Rapid boundaries are coalesced through a
one-item pending queue and rate-limited to protect screenshot cadence. Touch
capture and a session visual-tree provider must also be enabled.

For Simulator sessions, Studio can acknowledge `device.profile` with host
screenshot mode. The SDK then suspends periodic in-app JPEG capture for that
session so Studio can use a host-side source such as `simctl`. If the host does
not request that mode, the configured app capture loop continues.

## Screen route naming

Automatic screen capture uses the UIKit title, SwiftUI hosting root view type, or view-controller class name by default. Apps with custom routers can provide a resolver before activation to replace those names with semantic routes:

```swift
AnsightRuntime.shared.setScreenRouteResolver(
    AnsightScreenRouteResolver { context in
        guard context.swiftUIRootTypeName == "RootView" else {
            return nil
        }

        return AnsightScreenRoute(
            name: "Orders",
            key: "route:/orders",
            details: [
                "route": "/orders",
                "defaultScreen": context.defaultName
            ]
        )
    }
)

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
```

Returning `nil` or a blank route name keeps the default descriptor.

## Enrollment scanners

The aggregate `Ansight` product registers
`PlatformHostConnectionConfigReader` by default from
`initializeAndActivateAnsightSdk(...)`. For a physical device, run
`ansight pairing issue --qr`, then open its UIKit/AVFoundation QR scanner from a
developer-only app surface:

```swift
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
await AnsightRuntime.shared.connect(.qrCode(title: "Scan Ansight Enrollment QR"))
```

Lower-level `AnsightCore` integrations should add `AnsightPairingQR` and register the platform reader explicitly:

```swift
import AnsightCore
import AnsightPairingQR

AnsightRuntime.shared.setHostConnectionConfigReader(PlatformHostConnectionConfigReader())
```

Apps that use `.qrCode` must include `NSCameraUsageDescription` in `Info.plist`.

Apps that already own an enrollment scanner can provide their own reader. The
reader owns the UI, then returns the current one-use enrollment payload to the
normal `connect(...)` flow. `.file(...)` remains available for an approved
workflow that imports the same current invite as a file; bundled configuration
is not part of normal setup.

```swift
final class MyEnrollmentReader: HostConnectionConfigReading {
    func canRead(_ kind: HostConnectionRequestKind) -> Bool {
        kind == .file || kind == .qrCode
    }

    func readConfigPayload(for request: HostConnectionRequest) async throws -> String? {
        // Present a document picker, scanner, paste sheet, or app-owned import UI.
        nil
    }
}

AnsightRuntime.shared.setHostConnectionConfigReader(MyEnrollmentReader())
await AnsightRuntime.shared.connect(.qrCode(title: "Scan Ansight Enrollment QR"))
```

## Remote tools

Tool products are opt-in. Register only the surfaces you want exposed to Studio:

```swift
import AnsightCore
import AnsightToolsDatabase
import AnsightToolsFileDescriptorDiagnostics
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsReflection
import AnsightToolsSecureStorage
import AnsightToolsVisualTree

try AnsightRuntime.shared.registerPreferencesTools()
try AnsightRuntime.shared.registerFileDescriptorDiagnosticsTools()
try AnsightRuntime.shared.registerFileSystemTools()
try AnsightRuntime.shared.registerDatabaseTools()
try AnsightRuntime.shared.registerReflectionTools()
try AnsightRuntime.shared.registerSecureStorageTools()
try AnsightRuntime.shared.registerVisualTreeTools()
```

`AnsightToolsDatabase` opens SQLite files read-only inside approved app sandbox roots. `data.query` accepts one read-only SQL statement, clamps `maxRows` to `1...1000`, returns stable duplicate-column keys, and preserves ordered `rowValues` with SQLite storage types.

`AnsightToolsFileDescriptorDiagnostics` uses public process APIs to enumerate
open descriptors. Targets can be suppressed with
`AnsightFileDescriptorDiagnosticsOptions(includeTargets: false)` when paths or
socket identifiers should not be exposed.

`AnsightToolsReflection` exposes the shared `reflect.*` tool ids. Swift object
inspection uses `Mirror`; writes and method invocation require roots that
explicitly conform to `AnsightReflectionMutableRoot` and
`AnsightReflectionInvokableRoot`. `reflect.list_roots` includes `hostRuntime`
metadata with `kind: "swift"` for Swift/Objective-C hosted roots.

The visual-tree suite routes requests by source. Apps can register another
`AnsightVisualTreeProvider` with
`AnsightVisualTreeProviderRegistry.register(_:replaceExisting:)`; the built-in
UIKit source remains `native`.

Tool-suite options mirror the .NET allow-list/root concepts:

```swift
let fileOptions = AnsightFileSystemToolsOptions.createBuilder()
    .addRoot(alias: "exports", path: exportsDirectory.path)
    .build()
try AnsightRuntime.shared.registerFileSystemTools(options: fileOptions)

let databaseOptions = AnsightDatabaseToolsOptions.createBuilder()
    .addRoot(alias: "seeded", path: databaseDirectory.path)
    .includePlatformRoots(true)
    .build()
try AnsightRuntime.shared.registerDatabaseTools(options: databaseOptions)

let preferenceOptions = AnsightPreferencesToolOptionsBuilder()
    .withDefaultStore(Bundle.main.bundleIdentifier)
    .allowKeyPrefix("debug.")
    .build()
try AnsightRuntime.shared.registerPreferencesTools(options: preferenceOptions)

let secureOptions = AnsightSecureStorageToolsOptions.createBuilder()
    .withStorageIdentifier("com.example.app")
    .allowKey("debug_token")
    .allowKeyPrefix("ansight.")
    .build()
try AnsightRuntime.shared.registerSecureStorageTools(options: secureOptions)
```

For developer builds that should mirror the .NET `WithAnsightSdk` preset, depend on the aggregate `Ansight` product:

```swift
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
```

That preset keeps the core package tool-free by default, sets telemetry to
400 ms / 120 s retention, enables FPS, touch capture, 2-second JPEG capture at
quality 60 and max width 480 with GPU-backed surface capture enabled, enables
full tool access, and registers the current native tool suites, including
reflection. Reflection roots remain an explicit app registration boundary;
Swift writes and invocation require the opt-in root protocols described above.

## App Artifacts

Artifact providers expose requestable app snapshots such as reports, logs,
traces, or images:

```swift
struct ReportArtifactProvider: AnsightArtifactProvider {
    let descriptor = AnsightArtifactProviderDescriptor(
        id: "app.reports",
        name: "App Reports",
        category: "diagnostics"
    )

    func query(
        context: AnsightArtifactQueryContext
    ) throws -> [AnsightArtifactDefinition] {
        [
            AnsightArtifactDefinition(
                id: "current",
                name: "Current Report",
                description: "Exports the current diagnostic report.",
                kind: "report",
                category: "diagnostics",
                content: AnsightArtifactContentDescriptor(
                    supportedMimeTypes: ["text/plain"],
                    defaultMimeType: "text/plain",
                    suggestedFileName: "report.txt",
                    supportsText: true,
                    supportsBinary: true
                )
            ),
        ]
    }

    func create(request: AnsightArtifactRequest) throws -> AnsightArtifactResult {
        let text = buildCurrentReport()
        return AnsightArtifactResult(
            metadata: AnsightArtifactMetadata(
                artifactId: request.artifactId,
                providerId: request.providerId,
                name: "Current Report",
                kind: "report",
                mimeType: "text/plain",
                fileName: "report.txt"
            ),
            payload: .fromText(text)
        )
    }
}

try AnsightRuntime.shared.registerArtifactProvider(ReportArtifactProvider())
```

Registering the first provider adds the read-scoped `artifacts.query` and
`artifacts.request` tools automatically. Providers can also be passed through
`AnsightRemoteToolOptions.artifactProviders`. Requests require a live Studio
tool call; returned data is queued on the native binary-transfer channel.

## Custom Tools

Custom tools implement `AnsightTool` and are subject to the active
`AnsightToolGuard`.

```swift
import AnsightCore

struct StateSnapshotTool: AnsightTool {
    var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: "app.state.snapshot",
            name: "State Snapshot",
            description: "Returns current app state.",
            category: "app",
            scope: AnsightToolScope.read.rawValue,
            keywords: "state snapshot"
        )
    }

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        .success(.object(["state": .string("ready")]))
    }
}

try AnsightRuntime.shared.registerTool(StateSnapshotTool())
```

Use `replaceExisting: true` only for deliberate handler refreshes, such as a
bridge replacing a JavaScript-backed tool:

```swift
try AnsightRuntime.shared.registerTool(StateSnapshotTool(), replaceExisting: true)
```

The protocol bridge can process raw `tool.query` and `tool.call` envelopes:

```swift
let responseJson = try AnsightRuntime.shared.handleToolProtocolMessage(requestJson)
```

## Objective-C Facade

`AnsightObjC` exposes a smaller facade for Objective-C and React Native style
callers:

```objc
#import <AnsightObjC/AnsightObjC-Swift.h>

[ANSAnsight initializeAndActivateWithDefaultOptionsAndReturnError:&error];
[ANSAnsight recordMetric:42 channel:255 error:&error];
[ANSAnsight recordEventWithLabel:@"checkout"
                            type:@"Info"
                         details:@"loaded"
                         channel:255
                           error:&error];
```

The facade also exposes pairing, custom logs, session properties, metric
streams, and visual tree provider registration. Swift integrations should
prefer `AnsightRuntime.shared` directly.

## Status And Debugging

Use these APIs for diagnostics:

```swift
let status = AnsightRuntime.shared.hostConnectionStatus()
let subscription = AnsightRuntime.shared.addHostConnectionStatusListener { status, capabilities in
    // update host connection UI
}
let snapshot = AnsightRuntime.shared.snapshot()
let options = AnsightRuntime.shared.currentOptions()
let metrics = AnsightRuntime.shared.recordedMetrics()
let events = AnsightRuntime.shared.recordedEvents()
```

## Current limits

- `openSession(...)` remains a harness-only local compatibility API; use `connect(...)` or `openLiveSession(...)` for a real Studio session
- Swift reflection writes and method invocation require opt-in roots through `AnsightReflectionMutableRoot` and `AnsightReflectionInvokableRoot`
- SDK-owned file/QR pairing UI lives in the optional `AnsightPairingQR` product and is UIKit-only; macOS package builds compile the reader surface but report those request kinds unsupported
- binary file/screenshot transfer requires a live tool-protocol request context; direct in-process execution still reports a transfer-unavailable error
- public CocoaPods release publication still needs final source URL, signing, and versioning metadata
