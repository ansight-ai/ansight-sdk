# Ansight Android SDK

The Android SDK mirrors the .NET runtime concepts with Kotlin/Java-friendly
APIs. Use `ai.ansight:ansight-android` for the all-in-one developer setup, or
compose `ansight-core-android`, `ansight-pairing-android`, and individual tool
packages when you need a smaller runtime surface.

The native harness app lives in `harness/`.

For the guarded setup and verification workflow, see the
[Android getting-started guide](https://www.ansight.ai/docs/sdk/android/setup).

## Install

Use the all-in-one package for development builds:

```kotlin
dependencies {
    implementation("ai.ansight:ansight-android:1.4.0-preview.5")
}
```

Kotlin source projects need Kotlin Gradle plugin 1.8 or newer because the
published SDK uses Kotlin 1.8 metadata.

Minimal integrations can depend on only the packages they need:

```kotlin
dependencies {
    implementation("ai.ansight:ansight-core-android:1.4.0-preview.5")
    implementation("ai.ansight:ansight-pairing-android:1.4.0-preview.5")
    implementation("ai.ansight:ansight-tools-filedescriptordiagnostics-android:1.4.0-preview.5")
    implementation("ai.ansight:ansight-tools-jnireferencediagnostics-android:1.4.0-preview.5")
    implementation("ai.ansight:ansight-tools-visualtree-android:1.4.0-preview.5")
}
```

## Quickstart

Initialize from your `Application` only in the app's approved development or QA
variant:

```kotlin
import ai.ansight.Ansight
import android.app.Application

class MyApplication : Application() {
    override fun onCreate() {
        super.onCreate()

        if (BuildConfig.DEBUG) {
            Ansight.initializeAndActivateDeveloperMode(
                application = this,
                clientName = "Android App",
            )
        }
    }
}
```

Start the local host in one terminal and leave it running:

```sh
ansight host run
```

Launch the app on an emulator, then verify the connected session and tool
catalog from another terminal:

```sh
ansight session list --connected --json
ansight app tools <session-id> --json
```

An emulator registers automatically through loopback. No account, pairing
file, build constant, host address, or build-time host probe is required. If
the host is unavailable, the SDK keeps retrying without affecting the app.

For a physical device, issue a one-use QR from the running host, then scan it
from a developer-only screen:

```sh
ansight pairing issue --qr
```

```kotlin
Ansight.enrollFromQrCode(activity)
```

No prior app registration is required. The physical-device scan sends the
runtime package id, registers a random app-installation id, and stores the
resulting app-scoped registration in private storage. Later launches reconnect
automatically.
Google Code Scanner owns the camera interaction, so the app does not need the
`CAMERA` permission.

For unattended physical-device test runs, explicitly enable launch-time
provisioning in the test build:

```kotlin
val options = Ansight.developerOptions {
    withUnattendedProvisioning()
}

Ansight.initializeAndActivate(application, options)
```

The SDK then consumes the `ai.ansight.bootstrap.payload` string extra from the
first launcher Activity Intent, removes it immediately, enrolls through the
existing payload connection path, and saves the registration in app-private
storage. The option is disabled by default and should be enabled only in
development or test builds. A host runner can supply a fresh one-use payload
with `adb shell am start --es` without user input.

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

Native and custom HTTP integrations can submit a typed metadata record with
`AnsightRuntime.recordNetworkRequest(AnsightNetworkRequest(...))`. V1 has no
body fields. `AnsightNetworkRequestSanitizer` is always applied inside the
native runtime immediately before the record is transported, including records
received from React Native, Capacitor, Flutter, or .NET bridges.

## Options

`AnsightOptions` is the Android equivalent of .NET `Options`.

| Option | Purpose |
| --- | --- |
| `sampleFrequencyMilliseconds` | Built-in telemetry sampling interval. Clamped to 200-2000 ms. |
| `retentionPeriodSeconds` | Local metric/event retention window. Clamped to 60-3600 seconds. |
| `enableFramesPerSecond` | Enables `Choreographer` FPS sampling. |
| `enableBatteryLevel` | Enables battery sampling where available. |
| `enableOpenFileHandleTracking` | Enables process open-file-handle sampling. Disabled by default. |
| `enableJniReferenceCountTracking` | Registers the JNI reference-count channel for integrations that can supply it. Disabled by default. |
| `defaultMemoryChannels` | Selects Java heap, native heap, and RSS channels. |
| `additionalChannels` | Registers custom metric channels. Reserved ids are rejected. |
| `sessionJpegCapture` | Configures live JPEG screen-frame streaming and automatic visual-tree mode. `null` disables it. Includes `captureGpuBackedSurfaces` for cross-platform configuration parity. |
| `touchCapture` | Configures app-local touch capture. `null` disables it. |
| `toolGuard` | Controls remote-tool discovery and execution. |
| `customProperties` | Grouped string properties sent with `session.open`. |
| `hostAutoProbe` | Controls remembered-host retries after the host disappears and later reappears. |
| `hostConnection` | Configures enrollment reconnect and network policy. |
| `secureStorage` | Defines secure-storage allow-lists for the secure storage tools. |
| `initialTools` | Adds custom or package-provided tools at initialization. |
| `artifactProviders` | Adds app-defined artifact catalogs and binary exports. |

Use `withOpenFileHandleTracking()` to expose `Open File Handles` (reserved
channel 7) and sample the process descriptor table. `JNI reference count`
(reserved channel 6) is available through `withJniReferenceCountTracking()`
for integrations that can provide a tracked JNI count; the .NET Android bridge
supplies Java.Interop's global-reference count automatically. Both are disabled
by default and have matching `without...` builder methods.

`AnsightSessionJpegCaptureOptions.captureGpuBackedSurfaces` is accepted for
cross-platform configuration parity. It defaults to `true`; the capture-mode
tradeoff is currently meaningful on iOS, where setting it to `false` selects a
lower-overhead path that may miss GPU-backed surfaces.

Use `AnsightSessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch` to retain
periodic screenshots while capturing visual trees only on touch down and touch
up. Move and cancel events do not trigger capture. Rapid boundaries are
coalesced through a one-item pending queue and rate-limited to protect
screenshot cadence. Touch capture must also be enabled, and a session
visual-tree provider must be registered.

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

Automatic connection registers an emulator through its host address or uses a
physical device's saved enrollment.

Cellular host connections are disabled by default. Enrollment and reconnect
requests use the same restriction. Opt in only for a trusted development host
or personal hotspot:

```kotlin
val options = AnsightOptions.createBuilder()
    .withCellularHostConnections()
    .build()
```

The underlying `allowCellularConnections` option can consume mobile data and
allows connection attempts over a broader or carrier-managed network. Use it
only with a trusted development host.

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

The all-in-one package exposes QR enrollment for physical devices. Generate
the QR with `ansight pairing issue --qr`, then open the scanner:

```kotlin
Ansight.enrollFromQrCode(
    activity = activity,
    clientName = "Android App",
    expectedAppId = activity.packageName,
    onResult = { result ->
        // Inspect HostConnectionResult.
    },
)
```

If the app already owns a scanner, pass its result as an explicit payload:

```kotlin
AnsightRuntime.connect(
    HostConnectionRequest(
        kind = HostConnectionRequestKind.Payload,
        payload = enrollmentPayload,
        clientName = "Android App",
        expectedAppId = application.packageName,
    ),
)
```

No pairing file or build secret is required.

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

| Guard | Maximum policy |
| --- | --- |
| `AnsightToolGuard.Disabled` | None |
| `AnsightToolGuard.ReadOnly` | `Read` |
| `AnsightToolGuard.ReadWrite` | `Write` |
| `AnsightToolGuard.FullAccess` | `Critical` |

`ReadWrite` intentionally hides critical tools such as `files.delete_file`,
`prefs.remove_key`, secure-storage access, arbitrary reflection, and sensitive
UI operations.

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
| `ansight-tools-jnireferencediagnostics-android` | `AndroidJniReferenceDiagnosticsTools.create()` | `jni_references.capture_graph` bounded JNI-rooted heap graph |
| `ansight-tools-filesystem-android` | `AndroidFileSystemTools.create()` | `files.*` sandbox file tools |
| `ansight-tools-preferences-android` | `AndroidPreferencesTools.create()` | `prefs.*` SharedPreferences tools |
| `ansight-tools-securestorage-android` | `AndroidSecureStorageTools.create()` | `secure.*` allow-listed secure storage tools |
| `ansight-tools-database-android` | `AndroidDatabaseTools.create()` | `data.*` SQLite tools |
| `ansight-tools-reflection-android` | `AndroidReflectionTools.create()` | `reflect.*` registered-root reflection tools |

`reflect.list_roots` includes `hostRuntime` metadata with `kind: "jvm"` for
Android JVM/ART hosted roots.

Each tool package exposes a `*ToolIds` object that matches the .NET constants.

`jni_references.capture_graph` writes a temporary HPROF snapshot, indexes it
in-process, and returns only class names plus object-reference topology. Raw
object addresses and field values are never returned. The default graph is
bounded to 512 nodes, 1024 edges, and depth 4; callers can lower those bounds or
raise them up to the configured SDK limits. Heap capture briefly pauses the app
and can be expensive for large heaps, so invoke it as an explicit diagnostic
rather than on a sampling interval. The temporary HPROF file is deleted after
the result is built.

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
import ai.ansight.runtime.ToolPolicy
import org.json.JSONObject

AnsightRuntime.registerTool(
    FunctionAndroidTool(
        definition = ToolDefinition(
            id = "app.state.snapshot",
            name = "State Snapshot",
            description = "Returns current app state.",
            category = "app",
            policy = ToolPolicy.Read,
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

Configuring at least one provider automatically adds the `read` policy
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

All providers share the same generic automation tools: `ui.query_nodes`,
`ui.perform_action`, and `ui.wait`. Query results contain snapshot-scoped
references (`source`, `snapshotId`, `revision`, and `nodeId`). Re-query after a
`stale_node_reference` error before retrying an action. Flutter registers a
`flutter` provider, React Native registers `react`, and Capacitor registers
`dom`, so Host and CLI workflows do not need framework-specific tool ids.

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
