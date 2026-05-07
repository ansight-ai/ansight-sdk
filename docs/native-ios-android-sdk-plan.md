# Native iOS and Android SDK Plan

This plan is derived from the existing .NET SDK implementation. Existing Android, iOS, and React Native planning documents are intentionally not used as inputs.

## Objective

Build first-class native SDKs for Android and iOS that match the .NET SDK's runtime behavior, pairing protocol, telemetry model, screenshot streaming, tool protocol, security model, and remote tool surface wherever the behavior maps cleanly to native platforms.

React Native should come after these native SDKs. The React Native package should be a thin bridge over the Android and iOS SDKs, not a separate implementation of the protocol.

## .NET Source Of Truth

The source behavior comes from these .NET SDK areas:

- Core runtime: `src/dotnet/Ansight.Core/Runtime.cs`, `RuntimeImpl.cs`, `IRuntime.cs`
- Options and defaults: `src/dotnet/Ansight.Core/Options.cs`, `src/dotnet/Ansight/AnsightOptionsBuilderExtensions.cs`, `src/dotnet/Ansight.Maui/AnsightMauiOptionsBuilderExtensions.cs`
- Host connection and pairing: `src/dotnet/Ansight.Core/HostConnection*.cs`, `HostPairingManager.cs`, `HostSessionManager.cs`, `HostAutoProbeCoordinator.cs`
- Pairing protocol: `src/dotnet/Ansight.Core/Pairing`
- Telemetry model and streaming: `src/dotnet/Ansight.Core/Telemetry`
- Screenshot streaming: `src/dotnet/Ansight.Core/Screenshot`
- Tool protocol and security: `src/dotnet/Ansight.Core/Tools`
- Remote tool packages: `src/dotnet/Ansight.Tools.*`
- Build-time remote tool policy: `src/dotnet/Ansight.Build`
- Parity tests: `src/dotnet/Ansight.Tests`, `src/dotnet/Ansight.IntegrationTests`

## Product Shape

### Android

Package the Android SDK as Kotlin-first modules:

- `ansight-core-android`
- `ansight-tools-database`
- `ansight-tools-filesystem`
- `ansight-tools-preferences`
- `ansight-tools-securestorage`
- `ansight-tools-visualtree`
- `ansight-tools-reflection`, only after a native design review
- `ansight-gradle-plugin`, for developer pairing assets and remote tool build warnings

The core package should have no remote tools enabled by default. A convenience all-in-one package or preset can mirror .NET `WithAnsightSdk` behavior for developer builds.

### iOS

Package the iOS SDK as Swift-first products:

- `AnsightCore`
- `AnsightToolsDatabase`
- `AnsightToolsFileSystem`
- `AnsightToolsPreferences`
- `AnsightToolsSecureStorage`
- `AnsightToolsVisualTree`
- `AnsightToolsReflection`, only after a native design review
- SwiftPM build plugin and optional CocoaPods support for developer pairing assets and remote tool build warnings

The core package should have no remote tools enabled by default. A convenience all-in-one product or preset can mirror .NET `WithAnsightSdk` behavior for developer builds.

## Public API Target

The native APIs should feel idiomatic, but preserve .NET behavior.

Android sketch:

```kotlin
object AnsightRuntime {
    fun initialize(application: Application, options: AnsightOptions = AnsightOptions())
    fun initializeAndActivate(application: Application, options: AnsightOptions = AnsightOptions())
    fun activate()
    fun deactivate()
    fun clear()

    fun metric(value: Long, channel: Int = 255)
    fun event(
        label: String,
        type: AnsightEventType = AnsightEventType.Info,
        details: String? = null,
        channel: Int = 255,
    )
    fun screenViewed(screenName: String, details: String? = null, channel: Int = 255)
    fun setAppLifecycleState(state: AnsightAppLifecycleState)

    val hostConnection: AnsightHostConnection
    val toolBridge: AnsightToolProtocolBridge
}
```

iOS sketch:

```swift
public final class AnsightRuntime {
    public static let shared = AnsightRuntime()

    public func initialize(options: AnsightOptions = .init())
    public func initializeAndActivate(options: AnsightOptions = .init())
    public func activate()
    public func deactivate()
    public func clear()

    public func metric(_ value: Int64, channel: UInt8 = 255)
    public func event(
        _ label: String,
        type: AnsightEventType = .info,
        details: String? = nil,
        channel: UInt8 = 255
    )
    public func screenViewed(_ screenName: String, details: String? = nil, channel: UInt8 = 255)
    public func setAppLifecycleState(_ state: AnsightAppLifecycleState)

    public var hostConnection: AnsightHostConnection { get }
    public var toolBridge: AnsightToolProtocolBridge { get }
}
```

## Behavioral Requirements

### Runtime And Options

Match the .NET runtime lifecycle:

- Static/global runtime access.
- `Initialize`, `InitializeAndActivate`, `Activate`, `Deactivate`, and `Clear`.
- No-op metric/event calls before initialization where .NET is no-op.
- Active/inactive state controls host auto-connect, memory sampling, FPS sampling, screenshot streaming, and telemetry streaming.
- `OnActivated` and `OnDeactivated` equivalents.
- Current app lifecycle state with foreground/background/unknown.

Match option behavior:

- Core sample frequency default: 500 ms.
- Core retention default: 10 minutes.
- FPS enabled by default.
- Sample frequency clamped to 200-2000 ms.
- Retention clamped to 60-3600 seconds.
- Additional channels cannot use reserved channel IDs.
- Session JPEG capture disabled unless explicitly enabled.
- Session JPEG option defaults: 2000 ms interval, quality 60, max width 720.
- All-in-one developer preset should use the .NET all-in-one defaults: 400 ms sample frequency, 120 second retention, FPS enabled, host auto-probe enabled, and JPEG capture at 2000 ms, quality 60, max width 600.

### Channels And Telemetry

Preserve reserved channel IDs:

- `0`: primary managed/runtime heap channel.
- `1`: Android native heap or Apple physical footprint.
- `2`: Android RSS.
- `3`: FPS.
- `4`: lifecycle.
- `255`: not specified.

Implement the data sink behavior:

- Channel registry.
- Metric storage by channel.
- Event storage by channel.
- Retention trimming on mutation.
- Range queries.
- Snapshot support.
- Update notifications for metrics and events.
- Unknown metric channel calls are ignored.
- Event labels must be nonblank.
- Screen views are stored as app events.
- Lifecycle state changes generate lifecycle events.

### Host Connection

Match the .NET host connection model:

- `HasSavedConfig`
- `IsConnected`
- `Status`
- `Capabilities`
- `StatusChanged`
- `RefreshCapabilities`
- `TryParseConfigDocument`
- `Connect`
- `Disconnect`
- `ClearSavedConfigs`

Support connection request kinds:

- Auto
- Saved config
- Bundled config
- File
- QR code
- Payload
- Parsed config

Auto connection order should match .NET:

1. Already-connected session.
2. Bundled developer config.
3. Cached connected profile.
4. Saved config.
5. Standard bundled config.

Auto-probe should match .NET defaults:

- Enabled by default in the developer preset.
- Initial delay: 1 second.
- Probe interval: 5 seconds.
- Reconnect delay: 10 seconds.
- Disconnect when runtime deactivates.

### Pairing And Session Transport

Port the pairing document and compact-code behavior from .NET exactly:

- Canonical JSON.
- Signature validation.
- Expiration validation.
- Expected app ID validation.
- Compact config code parsing.
- Saved config persistence.
- Cached connected profile retention.
- Bad profile reset behavior.

Port the pairing transport:

- Wi-Fi preflight.
- UDP `CONNECT_REQ`.
- UDP `CONNECT_RESP`.
- 5 second UDP connect timeout.
- Only accept responses from the selected host address.
- Require WebSocket port, path, and token.
- WebSocket URL format: `ws://host:port/path?token=...`.
- WebSocket retry: 12 attempts, 2 second timeout per attempt, 250 ms delay.
- `CONTROL_REQ` and `CONTROL_RESP`.
- Request IDs shaped like `client.<guid>`.
- Serialized control requests.
- 15 second acks for `session.open`, `device.profile`, and app state.
- 10 second general send timeout.
- Clean shutdown of telemetry, app state, screenshots, tools, and transport.

### Device Profile

Implement native profile collectors equivalent to .NET:

- Device manufacturer/model/build/OS details.
- Locale and timezone.
- CPU architecture and core count.
- Display metrics.
- Battery/network where available.
- App ID, app name, version, build, process ID, debug/release environment.
- App icon capture with the same size and byte constraints.
- Runtime engine and stack metadata.
- Automatic profile plus caller override merge where caller values win.
- Normalized profile type/schema/sentAt/reason/profile sequence fields.

### Streaming

Telemetry stream must match .NET wire messages:

- `CLIENT_METRIC_CHANNELS`
- `CLIENT_METRICS`
- `CLIENT_EVENTS`
- Max metrics batch size: 160.
- Max pending metrics: 2000.
- Max events batch size: 160.
- Pump wake interval: 500 ms.
- Seed last 160 metrics on stream start.
- Announce new metric channels before sending metrics for those channels.

App state stream must match .NET:

- Send current state when session opens.
- Subscribe to lifecycle changes.
- Payload state values: `foreground`, `background`, `unknown`.
- Include `changedAtUtc`.
- Deduplicate repeated state.

JPEG stream must match .NET:

- Capture immediately, then on configured interval.
- Use the `ASJP` binary header.
- Header length: 28 bytes.
- Version: 1.
- Format: JPEG.
- Include quality, Unix timestamp in milliseconds, width, height, and JPEG byte count.
- Android capture from the current root view.
- iOS capture from the active key window.
- Preserve aspect ratio when scaling to max width.

### Tool Protocol

Port the tool protocol bridge:

- Capability: `tool.exec`.
- Actions: `tool.query`, `tool.catalog`, `tool.call`, `tool.result`, `tool.error`.
- Return `tool_protocol_invalid_request` for invalid tool protocol messages.
- Return `tool_runtime_not_initialized` when runtime is unavailable.
- Tool lookup is case-insensitive by ID.
- Duplicate tool IDs are rejected.
- Tool discovery and execution are separately guarded.
- Arguments are flattened to string values.
- Reserved execution arguments include request ID and optional session ID.
- Payload encoding should match .NET behavior.
- Binary transfer hub should support large payload follow-up transfers.

Port tool security:

- Disabled.
- Read-only.
- Read-write.
- Full access.
- Tool scopes and security levels should map to the .NET model.
- No execution should occur when no scopes are allowed.

### Remote Tool Suites

Implement native equivalents for the tool suites that map cleanly.

Database:

- `data.list_databases`
- `data.describe_schema`
- `data.query`
- SQLite-first.
- Encrypted database support is out of scope unless explicitly designed.

File system:

- `files.list_directory`
- `files.read_file`
- `files.get_file_checksum`
- `files.download_file`
- `files.begin_binary_download`
- `files.push_file`
- `files.copy_file`
- `files.move_file`
- `files.delete_file`
- Support sandboxed roots and the .NET checksum options.

Preferences:

- `prefs.list_keys`
- `prefs.get_value`
- `prefs.set_value`
- `prefs.remove_key`
- Android SharedPreferences.
- iOS UserDefaults.
- Restrict allowed stores, keys, and prefixes.

Secure storage:

- `secure.get_value`
- `secure.set_value`
- `secure.remove_key`
- Android Keystore-backed storage.
- iOS Keychain.
- Deny-all by default.
- Require explicit key or prefix allow lists.

Visual tree:

- `ui.get_visual_tree`
- `ui.get_screenshot`
- `ui.inspect_node`
- `ui.show_overlay`
- `ui.get_overlay`
- `ui.query_overlays`
- `ui.update_overlay`
- `ui.remove_overlay`
- `ui.clear_overlays`
- Native hierarchy inspection.
- Input-transparent overlays.
- Screenshot capture aligned with the session JPEG capture implementation.

Reflection:

- Do not blindly port .NET reflection.
- Treat Swift/Kotlin object inspection as a separate native design.
- Keep the .NET tool IDs reserved until a compatible and safe native model exists.

MAUI tools:

- Do not port MAUI-specific tools to native SDKs.
- Native SDKs should expose native UI and app inspection tools instead.

## Build-Time Safety

Mirror the .NET remote tool policy:

- Detect remote tool registrations at build time where possible.
- Default policy should allow with warnings in developer-oriented presets.
- Support explicit `Allowed`, `AllowedWithWarnings`, and `Disallowed` modes.
- Log detected remote tool types unless explicitly disabled.
- Fail builds when disallowed tools are present.

Android should implement this through a Gradle plugin. iOS should implement this through a SwiftPM build plugin, with CocoaPods support if needed for distribution.

## Phased Delivery

### Phase 0: Parity Fixtures

Create fixtures and tests from the .NET implementation:

- Option defaults and validation.
- Reserved channels.
- Pairing document parsing.
- Compact config code parsing.
- `CONNECT_REQ` and `CONNECT_RESP` payloads.
- Control envelopes.
- Device profile payloads.
- Telemetry payloads.
- `ASJP` screenshot frames.
- File transfer frames.
- Tool query and tool call envelopes.
- Tool guard decisions.

Deliverable: shared fixture suite that Android and iOS can run without the .NET runtime.

### Phase 1: Core Runtime

Implement Android and iOS core runtime APIs:

- Initialization.
- Activation/deactivation.
- Data sink.
- Metrics.
- Events.
- Screen views.
- Lifecycle state.
- Memory sampling.
- FPS sampling.
- Options validation.
- Runtime snapshots.

Acceptance: native runtime tests pass against .NET-derived fixtures.

### Phase 2: Host Connection And Pairing

Implement host connection and pairing managers:

- Bundled configs.
- Developer pairing configs.
- Saved configs.
- Cached profiles.
- Payload and QR code handling.
- Auto-connect.
- Auto-probe.
- Status transitions.
- Failure codes.

Acceptance: native SDKs can parse the same pairing payloads and produce the same connection decisions as .NET.

### Phase 3: Session Transport

Implement UDP and WebSocket session transport:

- Wi-Fi preflight.
- UDP bootstrap.
- WebSocket connection.
- Control request/response handling.
- Session open.
- Device profile send.
- Close and reconnect behavior.

Acceptance: Android and iOS sample apps can open and close sessions with the host using the same protocol as .NET.

### Phase 4: Streaming

Implement live streamers:

- Telemetry channels.
- Metrics.
- Events.
- App state.
- JPEG screenshots.

Acceptance: host receives native telemetry, lifecycle state, and screenshot frames indistinguishable from the .NET protocol.

### Phase 5: Tool Infrastructure

Implement the tool runtime:

- Tool registry.
- Tool schema validation.
- Tool guard.
- Tool protocol bridge.
- Payload encoding.
- Binary transfer hub.

Acceptance: host can query catalog and call test tools on both platforms with .NET-compatible envelopes.

### Phase 6: Tool Suites

Implement production native tools:

1. Preferences.
2. File system.
3. Database.
4. Secure storage.
5. Visual tree.
6. Reflection, only after design approval.

Acceptance: each tool suite passes parity tests for IDs, schemas, security, guard behavior, success envelopes, and error envelopes.

### Phase 7: Build Plugins And Distribution

Implement packaging and developer safety:

- Gradle plugin.
- SwiftPM build plugin.
- Developer pairing asset generation/embedding.
- Remote tool policy warnings/failures.
- Maven publishing.
- Swift Package release.
- CocoaPods release if required.

Acceptance: consuming apps can add the SDK, pair with the host, and receive build-time warnings for remote tools.

### Phase 8: Native Harnesses And Interop

Build full native harness apps:

- Android Kotlin harness.
- iOS Swift harness.
- Pairing UI.
- Runtime controls.
- Telemetry generation.
- Tool suite exercisers.
- Screenshot and visual tree validation.

Acceptance: harnesses pass live host interop tests and fixture-based protocol tests.

### Phase 9: React Native Bridge

Build the React Native SDK after native parity:

- Bridge initialization.
- Bridge activation/deactivation.
- Metrics/events/screen views.
- Host connection state.
- Pairing payload handoff.
- Tool enablement switches.

Acceptance: React Native does not duplicate pairing, transport, telemetry, screenshot, or tool protocol logic.

## First Milestone

The first implementation milestone should be an end-to-end native core slice on both Android and iOS:

1. Runtime init and activate.
2. Pairing payload parsing and validation.
3. UDP `CONNECT_REQ` and `CONNECT_RESP`.
4. WebSocket session open.
5. `session.open`.
6. `device.profile`.
7. Metric channel announcement.
8. Metrics/events streaming.
9. App lifecycle state streaming.
10. Clean disconnect.

Remote tools should start after this slice is stable.

## Guardrails

- Do not use current native planning docs as the implementation source of truth.
- Do not let React Native own protocol behavior.
- Do not enable remote tools from the core package by default.
- Do not skip saved configs, cached profiles, or auto-probe.
- Do not change wire protocol names, IDs, timeout semantics, or binary headers without updating the .NET SDK and host together.
- Do not port MAUI-specific tools to native SDKs.
- Do not port reflection until the native security and object model are explicitly designed.
