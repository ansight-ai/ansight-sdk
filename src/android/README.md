# Ansight Android SDK

The Android SDK mirrors the .NET runtime concepts with Kotlin/Java-friendly
APIs. Use `ai.ansight:ansight-android` for the all-in-one developer setup, or
compose `ansight-core-android`, `ansight-pairing-android`, and individual tool
packages when you need a smaller runtime surface.

The native harness app lives in `harness/`.

## Install

Use the all-in-one package for development builds:

```kotlin
dependencies {
    implementation("ai.ansight:ansight-android:1.0.2-preview.4")
}
```

Minimal integrations can depend on only the packages they need:

```kotlin
dependencies {
    implementation("ai.ansight:ansight-core-android:1.0.2-preview.4")
    implementation("ai.ansight:ansight-pairing-android:1.0.2-preview.4")
    implementation("ai.ansight:ansight-tools-filedescriptordiagnostics-android:1.0.2-preview.4")
    implementation("ai.ansight:ansight-tools-visualtree-android:1.0.2-preview.4")
}
```

## Quickstart

Initialize from your `Application`:

```kotlin
import ai.ansight.Ansight
import android.app.Application

class MyApplication : Application() {
    override fun onCreate() {
        super.onCreate()

        Ansight.initializeAndActivate(
            application = this,
            options = Ansight.developerOptions(
                bundledDeveloperConfigJson = BuildConfig.ANSIGHT_DEVELOPER_PAIRING_JSON,
                clientName = "Android App",
            ),
        )
    }
}
```

`Ansight.developerOptions(...)` applies the all-in-one developer preset:

- 400 ms sampling
- 120 second retention
- FPS enabled
- battery disabled
- JPEG capture every 2000 ms at quality 60 and max width 480
- touch capture enabled
- host auto-probe enabled
- full tool access
- all standard native tool suites registered

Use the core runtime directly when you do not want aggregate defaults:

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime

AnsightRuntime.initializeAndActivate(
    application = application,
    options = AnsightOptions(
        sampleFrequencyMilliseconds = 500,
        retentionPeriodSeconds = 600,
    ),
)
```

## Options

`AnsightOptions` is the Android equivalent of .NET `Options`.

| Option | Purpose |
| --- | --- |
| `sampleFrequencyMilliseconds` | Built-in telemetry sampling interval. Clamped to 200-2000 ms. |
| `retentionPeriodSeconds` | Local metric/event retention window. Clamped to 60-3600 seconds. |
| `enableFramesPerSecond` | Enables `Choreographer` FPS sampling. |
| `enableBatteryLevel` | Enables battery sampling where available. |
| `defaultMemoryChannels` | Selects Java heap, native heap, and RSS channels. |
| `additionalChannels` | Registers custom metric channels. Reserved ids are rejected. |
| `sessionJpegCapture` | Configures live JPEG screen-frame streaming. `null` disables it. Includes `captureGpuBackedSurfaces` for cross-platform configuration parity. |
| `touchCapture` | Configures app-local touch capture. `null` disables it. |
| `toolGuard` | Controls remote-tool discovery and execution. |
| `customProperties` | Grouped string properties sent with `session.open`. |
| `hostAutoProbe` | Controls remembered-host retries after the host disappears and later reappears. |
| `hostConnection` | Configures saved, bundled, and developer pairing sources. |
| `secureStorage` | Defines secure-storage allow-lists for the secure storage tools. |
| `initialTools` | Adds custom or package-provided tools at initialization. |
| `artifactProviders` | Adds app-defined artifact catalogs and binary exports. |

`AnsightSessionJpegCaptureOptions.captureGpuBackedSurfaces` is accepted for
cross-platform configuration parity. It defaults to `true`; the capture-mode
tradeoff is currently meaningful on iOS, where setting it to `false` selects a
lower-overhead path that may miss GPU-backed surfaces.

> **Important:** Screen capture will result in an FPS drop while the SDK
> captures, encodes, and sends frames. Use conservative interval, quality, and
> max-width settings, and disable `sessionJpegCapture` for performance-focused
> runs unless visual evidence is required.

For simulator/emulator sessions, Studio can acknowledge `device.profile` with
host screenshot mode. The SDK then suspends periodic in-app JPEG capture for
that session so Studio can use a host-side source such as `adb`. If the host
does not request that mode, the configured app capture loop continues.

## Host Connection

Runtime-owned host connection APIs live on `AnsightRuntime`.

```kotlin
val result = AnsightRuntime.connect(
    HostConnectionRequest(
        kind = HostConnectionRequestKind.Auto,
        clientName = "Android App",
    ),
)
```

Automatic connection tries:

1. `hostConnection.bundledDeveloperConfigJson`
2. remembered host profiles, newest first
3. saved pairing config
4. `hostConnection.bundledConfigJson`

Host auto-probe is enabled by default while the runtime is active. It remembers
previous host connections and retries them so the app can reconnect after the
host disappears and later reappears. Probing pauses while a live session is
connected and resumes after the retry delay when that session is lost:

```kotlin
val options = AnsightOptions.createBuilder()
    .withHostAutoProbe(
        AnsightHostAutoProbeOptions(
            enabled = true,
            initialDelayMilliseconds = 1_000,
            probeIntervalMilliseconds = 5_000,
            reconnectDelayMilliseconds = 10_000,
            clientName = "Android App",
        ),
    )
    .build()
```

Use `withoutHostAutoProbe()` for flows where reconnects should only happen
after an explicit app action.

Use explicit requests for override flows:

```kotlin
AnsightRuntime.connect(
    HostConnectionRequest(
        kind = HostConnectionRequestKind.Payload,
        payload = pairingJson,
        clientName = "Android App",
        expectedAppId = application.packageName,
    ),
)

AnsightRuntime.savePairingConfig(pairingJson, expectedAppId = application.packageName)
AnsightRuntime.clearSavedPairingConfig()
AnsightRuntime.clearCachedSession()
AnsightRuntime.disconnect()
```

The all-in-one package also exposes the Android pairing sheet:

```kotlin
Ansight.showPairingSheet(
    activity = activity,
    clientName = "Android App",
    expectedAppId = activity.packageName,
    onResult = { result ->
        // Inspect HostConnectionResult.
    },
)
```

Developer pairing JSON must not be shipped in release, CI, Play Store, or other
distributable builds.

## Telemetry

Register channels and record custom telemetry:

```kotlin
Ansight.registerMetricChannel(
    AnsightChannel(
        id = 42,
        name = "Cache",
        colorHex = "#FF9500",
        unit = "items",
        type = "cache",
    ),
)

AnsightRuntime.metric(12, channel = 42)
AnsightRuntime.event("cache_hit", AnsightEventType.Info, details = "warm=true", channel = 42)
AnsightRuntime.screenViewed("Orders", mapOf("route" to "/orders"))
AnsightRuntime.setAppLifecycleState(AppLifecycleState.Foreground)
```

For sampled custom values, register a metric stream:

```kotlin
Ansight.registerMetricStream(
    AnsightMetricStream(
        channel = AnsightChannel(43, "Queue Depth", unit = "items", type = "queue"),
        sampler = AnsightMetricSampler { queue.depth.toLong() },
    ),
)
```

## Logs And Session Properties

`sendClientLog` sends an app-provided line over the active live session:

```kotlin
val logResult = AnsightRuntime.sendClientLog("Checkout loaded cartId=debug-42")
```

SDK-internal logs can be observed with `AnsightLogger`:

```kotlin
val callback = AnsightLogCallback { level, message, throwable ->
    Log.d("Ansight", "[$level] $message", throwable)
}

AnsightLogger.registerCallback(callback)
AnsightLogger.removeCallback(callback)
```

Custom/session properties are grouped string values. They are included in the
next `session.open`; when a live session is connected, mutations are also sent
immediately with the `session.properties` control action.

```kotlin
Ansight.registerCustomProperty("app", "region", "au")
Ansight.removeCustomProperty("app", "region")
Ansight.clearCustomProperties()
```

## Tool Guards

| Guard | Allowed scopes |
| --- | --- |
| `AnsightToolGuard.Disabled` | None |
| `AnsightToolGuard.ReadOnly` | `Read` |
| `AnsightToolGuard.ReadWrite` | `Read`, `Write` |
| `AnsightToolGuard.FullAccess` | `Read`, `Write`, `Delete` |

`ReadWrite` intentionally hides delete-scoped tools such as
`files.delete_file`, `prefs.remove_key`, `secure.remove_key`, and overlay
removal tools.

## Runtime Toggles

FPS sampling can be changed after initialization:

```kotlin
if (!Ansight.isFramesPerSecondEnabled()) {
    Ansight.enableFramesPerSecond()
}

Ansight.disableFramesPerSecond()
```

Touch capture can be guarded by app state:

```kotlin
Ansight.setTouchCaptureGuard {
    sessionManager.isDebugSessionAllowed
}
```

## Standard Tool Packages

The aggregate `Ansight` package registers all standard tools unless the same
tool id is already present in `initialTools`.

| Package | Factory | Tool ids |
| --- | --- | --- |
| `ansight-tools-visualtree-android` | `AndroidVisualTreeTools.create()` | `ui.*` visual tree, screenshot, and overlay tools |
| `ansight-tools-filedescriptordiagnostics-android` | `AndroidFileDescriptorDiagnosticsTools.create()` | `file_descriptors.*` open descriptor diagnostics |
| `ansight-tools-filesystem-android` | `AndroidFileSystemTools.create()` | `files.*` sandbox file tools |
| `ansight-tools-preferences-android` | `AndroidPreferencesTools.create()` | `prefs.*` SharedPreferences tools |
| `ansight-tools-securestorage-android` | `AndroidSecureStorageTools.create()` | `secure.*` allow-listed secure storage tools |
| `ansight-tools-database-android` | `AndroidDatabaseTools.create()` | `data.*` SQLite tools |
| `ansight-tools-reflection-android` | `AndroidReflectionTools.create()` | `reflect.*` registered-root reflection tools |

`reflect.list_roots` includes `hostRuntime` metadata with `kind: "jvm"` for
Android JVM/ART hosted roots.

Each tool package exposes a `*ToolIds` object that matches the .NET constants.

Tool packages also expose .NET-style `AnsightOptionsBuilder` extensions:

```kotlin
import ai.ansight.withAnsightSdk
import ai.ansight.tools.filesystem.withFileSystemTools
import ai.ansight.tools.preferences.withPreferencesTools

val options = AnsightOptions.createBuilder()
    .withAnsightSdk {
        withFileSystemTools {
            addRoot("exports", application.filesDir.resolve("exports"))
        }
        withPreferencesTools {
            withDefaultStore("${application.packageName}_preferences")
            allowKeyPrefix("debug.")
        }
    }
    .build()
```

## Custom Tools

Register custom tools before or after initialization:

```kotlin
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.FunctionAndroidTool
import ai.ansight.runtime.ToolDefinition
import ai.ansight.runtime.ToolScope
import org.json.JSONObject

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

Use `replaceExisting = true` only for deliberate handler refreshes:

```kotlin
AnsightRuntime.registerTool(tool, replaceExisting = true)
```

## App Artifacts

Artifact providers expose requestable app snapshots such as reports, logs,
traces, or images. Add providers to the core options builder:

```kotlin
class ReportArtifactProvider : AndroidArtifactProvider {
    override val descriptor = AndroidArtifactProviderDescriptor(
        id = "app.reports",
        name = "App Reports",
        category = "diagnostics",
    )

    override fun query(
        context: AndroidArtifactQueryContext,
    ): List<AndroidArtifactDefinition> = listOf(
        AndroidArtifactDefinition(
            id = "current",
            name = "Current Report",
            description = "Exports the current diagnostic report.",
            kind = "report",
            category = "diagnostics",
            mimeType = "text/plain",
            fileName = "report.txt",
        ),
    )

    override fun create(request: AndroidArtifactRequest): AndroidArtifactResult {
        val bytes = buildCurrentReport().toByteArray()
        return AndroidArtifactResult(
            metadata = AndroidArtifactMetadata(
                providerId = descriptor.id,
                artifactId = request.artifactId,
                name = "Current Report",
                kind = "report",
                mimeType = "text/plain",
                fileName = "report.txt",
                sizeBytes = bytes.size.toLong(),
            ),
            bytes = bytes,
        )
    }
}

val options = AnsightOptions.createBuilder()
    .addArtifactProvider(ReportArtifactProvider())
    .withReadOnlyToolAccess()
    .build()
```

Configuring at least one provider automatically adds the read-scoped
`artifacts.query` and `artifacts.request` tools. Requests require a live Studio
tool call; returned bytes are sent over the native binary-transfer channel.
Provider query failures are isolated in the artifact catalog.

## Visual Tree Providers

The visual tree tools route by source. The native provider is registered by the
standard Android tool suite. Apps can add additional sources:

```kotlin
AndroidVisualTreeProviderRegistry.register(myProvider, replaceExisting = true)
```

React Native registers a `react` provider through `@ansight/react-native`.

## Status And Debugging

Use these APIs for diagnostics:

```kotlin
val runtimeSnapshot = AnsightRuntime.snapshot()
val status = AnsightRuntime.hostConnectionStatus()
val options = AnsightRuntime.options()
val metrics = AnsightRuntime.recordedMetrics()
val events = AnsightRuntime.recordedEvents()
```

`HostConnectionResult` and `OperationResult` return `success`, `message`, and
machine-readable context where available.

## Local Publication

Create `local.properties` in this directory with your Android SDK path when
`ANDROID_HOME` or `ANDROID_SDK_ROOT` is not already configured.

Publish release AARs to Maven local:

```bash
./gradlew publishReleasePublicationToMavenLocal
```

You can override publication coordinates:

```bash
./gradlew :ansight:publishReleasePublicationToMavenLocal \
  -PansightAndroidGroup=com.example \
  -PansightAndroidArtifactId=ansight-android \
  -PansightAndroidVersion=1.0.0-local
```

## Validation

Run the SDK unit tests and native harness build:

```bash
./gradlew :ansight-core:test
./gradlew :ansight:test
./gradlew :harness:assembleDebug
```

The broader Android corpus validator lives at:

```bash
python ../../scripts/validate_android_test_apps.py --help
```

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.
