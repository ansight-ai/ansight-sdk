# Ansight SDK API Parity

This guide is the cross-SDK reference for public Ansight runtime APIs. The .NET
SDK remains the naming and behavior baseline; Android, iOS, and React Native
should expose the same concepts with platform-native conventions.

## Package Map

| Capability | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Core runtime | `Ansight.Core` | `ai.ansight:ansight-core-android` | `AnsightCore` | Native dependency |
| All-in-one runtime | `Ansight` | `ai.ansight:ansight-android` | `Ansight` | `@ansight/react-native` |
| Pairing UI | `Ansight.Pairing` | `ai.ansight:ansight-pairing-android` | `AnsightPairingQR` | Native bridge |
| Visual tree tools | `Ansight.Tools.VisualTree` | `ai.ansight:ansight-tools-visualtree-android` | `AnsightToolsVisualTree` | Native tools plus React tools |
| File tools | `Ansight.Tools.FileSystem` | `ai.ansight:ansight-tools-filesystem-android` | `AnsightToolsFileSystem` | Native bridge |
| Preferences tools | `Ansight.Tools.Preferences` | `ai.ansight:ansight-tools-preferences-android` | `AnsightToolsPreferences` | Native bridge |
| Secure storage tools | `Ansight.Tools.SecureStorage` | `ai.ansight:ansight-tools-securestorage-android` | `AnsightToolsSecureStorage` | Native bridge |
| Database tools | `Ansight.Tools.Database` | `ai.ansight:ansight-tools-database-android` | `AnsightToolsDatabase` | Native bridge |
| Reflection tools | `Ansight.Tools.Reflection` | `ai.ansight:ansight-tools-reflection-android` | `AnsightToolsReflection` | Native bridge |

## All-In-One Defaults

The all-in-one/developer preset is intended to be equivalent across .NET,
Android, and iOS:

| Setting | Value |
| --- | --- |
| Sampling | 400 ms |
| Retention | 120 seconds |
| FPS | Enabled |
| Battery | Disabled |
| JPEG capture | Enabled, 2000 ms, quality 60, max width 480; iOS GPU-backed surface capture defaults to enabled |
| Touch capture | Enabled |
| Host auto-probe | Enabled |
| Tool guard | Full access in native all-in-one presets |
| Bundled developer config | Preferred over saved and plain bundled configs |
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

## Quickstart Equivalents

.NET:

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithBundledHostConnection(typeof(App).Assembly);
    })
    .Build();

Runtime.InitializeAndActivate(options);
```

Android:

```kotlin
import ai.ansight.Ansight

Ansight.initializeAndActivate(
    application = application,
    options = Ansight.developerOptions(
        bundledDeveloperConfigJson = BuildConfig.ANSIGHT_DEVELOPER_PAIRING_JSON,
        clientName = "Android App",
    ),
)
```

iOS:

```swift
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
await AnsightRuntime.shared.connect(.auto(clientName: "iOS App"))
```

React Native:

```ts
import Ansight from "@ansight/react-native";

const isDevelopmentOnly = __DEV__;

await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: isDevelopmentOnly,
  clientName: "React Native App",
  hostConnection: isDevelopmentOnly ? {
    bundledDeveloperConfigJson: process.env.EXPO_PUBLIC_ANSIGHT_PAIRING_CONFIG_JSON,
  } : undefined,
  toolGuard: isDevelopmentOnly ? "readOnly" : "disabled",
});

await Ansight.connect(null, { clientName: "React Native App" });
```

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
| Custom log | `PairingSessionClient.SendClientLogAsync(...)` | `sendClientLog(line)` | `sendClientLog(_:)` | `sendClientLog(line)` |
| Status | `Runtime.HostConnection.Status` | `hostConnectionStatus()` | `hostConnectionStatus()` | `hostConnectionStatus()` |
| Capabilities | `Runtime.HostConnection.Capabilities` | `hostConnectionCapabilities()` | `hostConnectionCapabilities()` | `hostConnectionCapabilities()` |
| Config changed | `NotifyConfigChangedAsync()` | `notifyHostConnectionConfigChanged()` | `notifyHostConnectionConfigChanged()` | `notifyHostConnectionConfigChanged()` |
| Status listener | `Runtime.HostConnection.StatusChanged` | `addHostConnectionStatusListener(...)` | `addHostConnectionStatusListener(...)` | `addHostConnectionStatusListener(...)` |
| Snapshot | `Runtime.Instance.DataSink` / debug state | `snapshot()` | `snapshot()` | `snapshot()` |
| Current options | Options instance | `options()` | `currentOptions()` | `currentOptions()` |
| FPS toggle/status | `IsFramesPerSecondEnabled`, `EnableFramesPerSecond()`, `DisableFramesPerSecond()` | `isFramesPerSecondEnabled()`, `enableFramesPerSecond()`, `disableFramesPerSecond()` | `isFramesPerSecondEnabled`, `enableFramesPerSecond()`, `disableFramesPerSecond()` | `isFramesPerSecondEnabled()`, `enableFramesPerSecond()`, `disableFramesPerSecond()` |
| Touch capture guard/status | `IsTouchCaptureEnabled`, `SetTouchCaptureGuard(...)` | `isTouchCaptureEnabled()`, `setTouchCaptureGuard(...)` | `isTouchCaptureEnabled`, `setTouchCaptureGuard(...)` | Native bridge exposes enable/disable; JS guard is app-owned |
| SDK log hook | `Logger` callback APIs | `AnsightLogger.registerCallback(...)` | `AnsightLogger.registerCallback(...)` | `addLogListener(...)` |

## Option Map

| Concept | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Sample interval | `WithSampleFrequencyMilliseconds(...)` | `AnsightOptions.sampleFrequencyMilliseconds` | `AnsightOptions.sampleFrequencyMilliseconds` | `sampleFrequencyMilliseconds` |
| Retention | `WithRetentionPeriodSeconds(...)` | `retentionPeriodSeconds` | `retentionPeriodSeconds` | `retentionPeriodSeconds` |
| FPS | `WithFramesPerSecond()` | `enableFramesPerSecond` | `enableFramesPerSecond` | `enableFramesPerSecond` |
| Battery | `WithBatteryLevel()` | `enableBatteryLevel` | `enableBatteryLevel` | `enableBatteryLevel` |
| Memory channels | `DefaultMemoryChannels` | `defaultMemoryChannels` | `defaultMemoryChannels` | `defaultMemoryChannels` |
| Custom channels | `AddAdditionalChannel(...)` | `additionalChannels` | `additionalChannels` | `additionalChannels` |
| JPEG capture | `WithSessionJpegCapture(...)` | `sessionJpegCapture` with `captureGpuBackedSurfaces` parity flag | `sessionJpegCapture.captureGpuBackedSurfaces` | `sessionJpegCapture.captureGpuBackedSurfaces` |
| Touch capture | `WithTouchCapture(...)` | `touchCapture` | `touchCapture` | `touchCapture` |
| Tool guard | `WithReadOnlyToolAccess()` etc. | `toolGuard` | `toolGuard` | `toolGuard` |
| Custom properties | `WithCustomProperties(...)` / runtime mutations | `customProperties` | `customProperties` | `customProperties` |
| Host auto-probe | `WithHostAutoProbe(...)` | `hostAutoProbe` | `hostAutoProbe` | `hostAutoProbe` |
| Host connection | `ConfigureHostConnection(...)` | `hostConnection` | `hostConnection` | `hostConnection` |
| Native tool suites | `With...Tools(...)` options | `with...Tools(...)` options | `AnsightRemoteToolOptions` | `remoteTools` |

## Host Connection

Automatic connection resolves candidate configs in this order:

1. Bundled developer config.
2. Remembered cached host profiles where implemented.
3. Saved config.
4. Plain bundled config.

Explicit requests such as payload, QR, file, saved, and bundled config bypass
that default order and use the requested source.

| Concept | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| Auto connect | `HostConnectionRequest.Auto()` | `HostConnectionRequest()` or `Auto` | `.auto(...)` | `connect(null, options)` |
| Payload connect | `PayloadText(json)` | `HostConnectionRequest.payload(json)` | `.payloadText(json, ...)` | `connect(json, options)` |
| Saved config | `SaveConfigAsync(...)` / `SavedConfig()` | `savePairingConfig(...)` / `SavedConfig` | `savePairingConfig(...)` / `.savedConfig()` | `savePairingConfig(...)` |
| Clear saved | `ClearSavedConfigAsync()` | `clearSavedPairingConfig()` | `clearSavedPairing()` | `clearSavedPairing()` |
| Clear cached profile | `ClearCachedSessionAsync()` | `clearCachedSession()` | `clearCachedSession()` | `clearCachedSession()` |
| Developer bundled config | `ansight.developer-pairing.json` | `bundledDeveloperConfigJson` | `bundledDeveloperConfigJson` | `hostConnection.bundledDeveloperConfigJson` |
| Plain bundled config | `ansight.json` | `bundledConfigJson` | `bundledConfigJson` | `hostConnection.bundledConfigJson` |
| Expected app id | Request option | `expectedAppId` on save/open paths | request option | `expectedAppId` |
| Host override | Request option | `hostAddressOverride` | request option | `hostAddressOverride` |
| Simulator host fallback | yes | Android emulator host address | iOS Simulator localhost | inherited from native runtime |

Use developer pairing only for local development. Release, CI, TestFlight, App
Store, Play Store, and other distributable builds should not embed developer
pairing resources.

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

The protocol tool ids are shared by .NET, Android, and iOS:

| Suite | Tool ids |
| --- | --- |
| Visual tree | `ui.get_visual_tree`, `ui.get_screenshot`, `ui.inspect_node`, `ui.show_overlay`, `ui.get_overlay`, `ui.query_overlays`, `ui.update_overlay`, `ui.remove_overlay`, `ui.clear_overlays` |
| Files | `files.list_directory`, `files.read_file`, `files.get_file_checksum`, `files.download_file`, `files.begin_binary_download`, `files.push_file`, `files.copy_file`, `files.move_file`, `files.delete_file` |
| Preferences | `prefs.list_keys`, `prefs.get_value`, `prefs.set_value`, `prefs.remove_key` |
| Secure storage | `secure.get_value`, `secure.set_value`, `secure.remove_key` |
| Database | `data.list_databases`, `data.describe_schema`, `data.query` |
| Reflection | `reflect.list_roots`, `reflect.inspect_object`, `reflect.describe_type`, `reflect.set_member_value`, `reflect.invoke_method` |
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
[Remote Tool Protocol](protocol/tools.md).

Tool-suite registration follows the same app-code convention:

| Suite | .NET | Android | iOS | React Native |
| --- | --- | --- | --- | --- |
| All current native tools | `WithAnsightRemoteTools()` | `withAnsightRemoteTools()` | `registerAnsightRemoteTools(options:)` | `remoteTools` |
| Developer defaults and tools | `WithAnsightSdk(...)` | `withAnsightSdk { ... }` or `Ansight.developerOptions(...)` | `initializeAndActivateAnsightSdk(...)` | `withAnsightSdk(...)` |
| Visual tree | `WithVisualTreeTools()` | `withVisualTreeTools()` | `registerVisualTreeTools()` | native default plus `installReactTools(...)` |
| Files | `WithFileSystemTools(...)` | `withFileSystemTools { addRoot(...) }` | `AnsightFileSystemToolsOptions.createBuilder()` | `remoteTools.fileSystem` |
| Preferences | `WithPreferencesTools(...)` | `withPreferencesTools { allowKey(...) }` | `AnsightPreferencesToolOptions.createBuilder()` | `remoteTools.preferences` |
| Secure storage | `WithSecureStorageTools(...)` | `withSecureStorageTools { allowKey(...) }` | `AnsightSecureStorageToolsOptions.createBuilder()` | `remoteTools.secureStorage` |
| Database | `WithDatabaseTools()` | `withDatabaseTools { includePlatformRoots(...) }` | `AnsightDatabaseToolsOptions.createBuilder()` | `remoteTools.database` |
| Reflection | `WithReflectionTools(...)` | `withReflectionTools { allowRoot(...) }` | `AnsightReflectionToolsOptions.createBuilder()` | `remoteTools.reflection` |

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

## Validation Commands

Run these checks after changing SDK API docs or examples:

```bash
cd /Users/matthewrobbins/Development/git/ansight-sdk/src/android
./gradlew :ansight-core:test :ansight:test :harness:assembleDebug

cd /Users/matthewrobbins/Development/git/ansight-sdk/src/ios
swift test

cd /Users/matthewrobbins/Development/git/ansight-sdk/src/react-native
npm run check
```

For broader corpus validation, use:

```bash
python /Users/matthewrobbins/Development/git/ansight-sdk/scripts/validate_android_test_apps.py --help
python /Users/matthewrobbins/Development/git/ansight-sdk/scripts/validate_ios_test_apps.py --help
```
