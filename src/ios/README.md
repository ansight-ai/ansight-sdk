# Ansight iOS

The native harness app lives in `Examples/NativeHarness/`.

Import `AnsightCore` plus individual pairing/tool products for a minimal integration, or import the aggregate `Ansight` product for developer defaults, QR/file pairing UI, and the current native remote-tool suites.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Current capabilities

- runtime initialization, activation, deactivation, clearing, manual and automatic UIKit lifecycle state, screen views, custom metrics, custom events, and structured debug snapshots
- shared telemetry bounds: 200-2000ms sampling, 60-3600s retention, reserved channel validation, bounded retained metric/event buffers
- pairing config parsing and validation for `ansight.pairing-config.v1`, `ansight.pairing-config-document.v1`, and legacy `ansight.pairing-ticket.v1`
- discovery-hint host resolution for local developer flows
- host connection status/results for auto, saved, bundled, payload, direct config, and app-provided file/QR config-reader sources
- UDP `CONNECT_REQ` bootstrap and WebSocket handoff compatible with the .NET SDK transport contract
- live control messages for `session.open`, `device.profile`, and `app.state`
- live metric channel, metric sample, and app event streaming using the established `CLIENT_METRIC_CHANNELS`, `CLIENT_METRICS`, and `CLIENT_EVENTS` payloads
- automatic UIKit foreground/background, UIKit view-controller screen-view, and SwiftUI `UIHostingController` root-view capture with explicit opt-out controls and app-provided route naming hooks
- FPS telemetry sampling through `CADisplayLink` on UIKit platforms using the reserved FPS metric channel
- live JPEG screen-frame capture using Studio's binary `ASJP` / `CLIENT_JPEG` WebSocket path
- live UIKit touch capture using a simultaneous window gesture recognizer and Studio-compatible `CLIENT_TOUCH_INPUT` / `ansight.touches.v1` packed batches
- baseline Apple device/app profile collection without direct PII, including runtime stack codes, app icon payloads, Metal GPU/render-backend details, and coarse network transport
- Keychain-backed saved pairing config storage and remembered host profiles with explicit clearing
- queued `ansight.file-transfer.v1` binary artifact transfers for screenshot and file-download tools during live Studio sessions
- executable tool registration, tool guard policy, tool security metadata, reserved tool call context arguments, and `tool.query` / `tool.call` protocol handling
- `AnsightToolsPreferences` SwiftPM product with `prefs.list_keys`, `prefs.get_value`, `prefs.set_value`, and `prefs.remove_key` for sandboxed `UserDefaults` access
- `AnsightToolsFileSystem` SwiftPM product with sandboxed directory listing, file read/checksum/download, live binary download, push/copy/move/delete tools
- `AnsightToolsDatabase` SwiftPM product with SQLite discovery, schema inspection, and constrained read-only query tools: `data.list_databases`, `data.describe_schema`, and `data.query`
- `AnsightToolsSecureStorage` SwiftPM product with allow-listed Keychain `secure.get_value`, `secure.set_value`, and `secure.remove_key` tools
- `AnsightToolsReflection` SwiftPM product with registered-root `reflect.*` inspection tools and opt-in write/invoke hooks
- `AnsightToolsVisualTree` SwiftPM product with UIKit visual tree, node inspection, live screenshot, and diagnostic overlay tools
- `AnsightPairingQR` SwiftPM product with UIKit document import and AVFoundation QR scanning for SDK-owned pairing UI
- SwiftPM build-time developer pairing generation and bundled-tool enforcement

## SwiftPM developer mode

When building this package through SwiftPM, the `AnsightBuildToolPlugin` runs automatically for the `AnsightCore` target.

Environment variables:

- `ANSIGHT_DEVELOPER_PAIRING_ENABLED=true`
- `ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE=/absolute/path/to/ansight.json` (optional; defaults to `src/ios/ansight.json` when present)
- `ANSIGHT_ALLOW_REMOTE_TOOLS=true` to permit bundled `AnsightTool` implementations

With developer pairing enabled, the build tool reads the source pairing config, captures local host metadata when available, and generates an embedded pairing ticket that you can access at runtime through `AnsightDeveloperMode.embeddedPairingJson`.

Without `ANSIGHT_ALLOW_REMOTE_TOOLS=true`, the build fails when the target source contains concrete `AnsightTool` conformances.

## CocoaPods

The iOS SDK also ships local podspecs that mirror the SwiftPM products:

```ruby
pod 'Ansight', :path => '/path/to/ansight-sdk/src/ios'
```

For minimal integrations, depend on only the modules you need:

```ruby
pod 'AnsightCore', :path => '/path/to/ansight-sdk/src/ios'
pod 'AnsightPairingQR', :path => '/path/to/ansight-sdk/src/ios'
pod 'AnsightToolsFileSystem', :path => '/path/to/ansight-sdk/src/ios'
pod 'AnsightToolsReflection', :path => '/path/to/ansight-sdk/src/ios'
pod 'AnsightToolsVisualTree', :path => '/path/to/ansight-sdk/src/ios'
```

The aggregate `Ansight` pod depends on `AnsightCore`, `AnsightPairingQR`, `AnsightToolsDatabase`, `AnsightToolsFileSystem`, `AnsightToolsPreferences`, `AnsightToolsReflection`, `AnsightToolsSecureStorage`, and `AnsightToolsVisualTree`.

The `AnsightCore` pod runs the same developer build-artifact generator before compile. It honors `ANSIGHT_DEVELOPER_PAIRING_ENABLED`, `ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE`, and `ANSIGHT_ALLOW_REMOTE_TOOLS`, then writes the pod-only `AnsightGeneratedBuildArtifactsProvider` used by `AnsightDeveloperMode`.

## Quickstart

Use the aggregate product for the developer preset and all current native tool
suites:

```swift
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
await AnsightRuntime.shared.connect(.auto(clientName: "iOS App"))
```

The aggregate preset mirrors the .NET all-in-one defaults:

- 400 ms sampling
- 120 second retention
- FPS enabled
- battery disabled
- UIKit lifecycle capture enabled
- JPEG capture every 2000 ms at quality 60 and max width 480
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
| `lifecycleCapture` | Controls automatic UIKit app lifecycle and screen-view capture. |
| `sessionJpegCapture` | Configures live JPEG screen-frame streaming. `nil` disables it. |
| `touchCapture` | Configures app-local touch capture. `nil` disables it. |
| `toolGuard` | Controls remote-tool discovery and execution. |
| `customProperties` | Grouped string properties sent with `session.open`. |
| `hostAutoProbe` | Controls automatic host reconnect attempts. |
| `hostConnection` | Configures saved, cached, bundled, and developer pairing sources. |

Host connection options:

```swift
var options = AnsightOptions.ansightDeveloperDefaults
options.hostConnection = AnsightHostConnectionOptions(
    savedConfigKey: "ai.ansight.ios.saved-pairing",
    connectionProfileRetentionSeconds: 14 * 24 * 60 * 60,
    discoveryPort: 45123,
    bundledDeveloperConfigJson: AnsightDeveloperMode.embeddedPairingJson,
    bundledConfigJson: nil
)
```

`bundledDeveloperConfigJson` is the local-development source. `bundledConfigJson`
is the plain bundled fallback. Do not ship developer pairing resources in
release, CI, TestFlight, App Store, or other distributable builds.

## Host Connection

Runtime-owned host connection APIs live on `AnsightRuntime.shared`.

```swift
let result = await AnsightRuntime.shared.connect(
    .auto(clientName: "iOS App")
)
```

Automatic connection tries:

1. `hostConnection.bundledDeveloperConfigJson` or the embedded developer pairing artifact.
2. remembered cached host profiles, newest first.
3. saved pairing config.
4. `hostConnection.bundledConfigJson`.

Use explicit requests for override flows:

```swift
await AnsightRuntime.shared.connect(
    .payloadText(
        pairingJson,
        clientName: "iOS App",
        expectedAppId: Bundle.main.bundleIdentifier
    )
)

AnsightRuntime.shared.savePairingConfig(pairingJson)
AnsightRuntime.shared.clearSavedPairing()
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

Configure `AnsightOptions.sessionJpegCapture` before connecting to Studio:

```swift
try AnsightRuntime.shared.initializeAndActivate(
    options: AnsightOptions(
        sessionJpegCapture: AnsightSessionJpegCaptureOptions(
            intervalMilliseconds: 2_000,
            quality: 60,
            maxWidth: 480
        )
    )
)
await AnsightRuntime.shared.connect(.auto(clientName: "iOS Native Harness"))
```

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

## File and QR pairing payloads

The aggregate `Ansight` product registers `PlatformHostConnectionConfigReader` by default from `initializeAndActivateAnsightSdk(...)`. It presents a UIKit document picker for `.file` requests and a UIKit/AVFoundation QR scanner for `.qrCode` requests:

```swift
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
await AnsightRuntime.shared.connect(.qrCode(title: "Scan Pairing QR"))
```

Lower-level `AnsightCore` integrations should add `AnsightPairingQR` and register the platform reader explicitly:

```swift
import AnsightCore
import AnsightPairingQR

AnsightRuntime.shared.setHostConnectionConfigReader(PlatformHostConnectionConfigReader())
```

Apps that use `.qrCode` must include `NSCameraUsageDescription` in `Info.plist`.

Apps that need custom pairing UI can provide their own reader. The reader owns the UI, then returns the pairing payload string to the normal `connect(...)` flow:

```swift
final class MyPairingReader: HostConnectionConfigReading {
    func canRead(_ kind: HostConnectionRequestKind) -> Bool {
        kind == .file || kind == .qrCode
    }

    func readConfigPayload(for request: HostConnectionRequest) async throws -> String? {
        // Present a document picker, scanner, paste sheet, or app-owned import UI.
        nil
    }
}

AnsightRuntime.shared.setHostConnectionConfigReader(MyPairingReader())
await AnsightRuntime.shared.connect(.qrCode(title: "Scan Pairing QR"))
```

## Remote tools

Tool products are opt-in. Register only the surfaces you want exposed to Studio:

```swift
import AnsightCore
import AnsightToolsDatabase
import AnsightToolsFileSystem
import AnsightToolsPreferences
import AnsightToolsReflection
import AnsightToolsSecureStorage
import AnsightToolsVisualTree

try AnsightRuntime.shared.registerPreferencesTools()
try AnsightRuntime.shared.registerFileSystemTools()
try AnsightRuntime.shared.registerDatabaseTools()
try AnsightRuntime.shared.registerReflectionTools()
try AnsightRuntime.shared.registerSecureStorageTools()
try AnsightRuntime.shared.registerVisualTreeTools()
```

`AnsightToolsDatabase` opens SQLite files read-only inside approved app sandbox roots. `data.query` accepts one read-only SQL statement, clamps `maxRows` to `1...1000`, returns stable duplicate-column keys, and preserves ordered `rowValues` with SQLite storage types.

`AnsightToolsReflection` exposes the shared `reflect.*` tool ids. Swift object
inspection uses `Mirror`; writes and method invocation require roots that
explicitly conform to `AnsightReflectionMutableRoot` and
`AnsightReflectionInvokableRoot`.

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

That preset keeps the core package tool-free by default, sets telemetry to 400 ms / 120 s retention, enables FPS, touch capture, 2-second JPEG capture at quality 60 and max width 480, enables full tool access, and registers the current native tool suites. Reflection is intentionally excluded until the native security and object model are designed.

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
