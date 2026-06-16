# Ansight Android

The native Android SDK plan lives in [../../docs/native-ios-android-sdk-plan.md](../../docs/native-ios-android-sdk-plan.md).

The native harness app lives in `harness/`.

## Current first pass

The first native Android runtime pass covers SDK goals 00 through 06:

- runtime/options/lifecycle/screen-view integration
- pairing config parsing and ECDSA signature verification
- structured host connection status/results
- UDP bootstrap plus WebSocket control transport
- Android device/app profile collection
- bounded built-in/custom telemetry capture

See [../../docs/android-sdk-gap-analysis.md](../../docs/android-sdk-gap-analysis.md)
for validation evidence and remaining gaps.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Packages

The Android SDK mirrors the .NET package split:

- `ai.ansight:ansight-core-android` contains the core runtime, telemetry, events, host connection, and transport concepts.
- `ai.ansight:ansight-pairing-android` contains native QR pairing acquisition and the Android bottom-sheet pairing UI.
- `ai.ansight:ansight-tools-visualtree-android`
- `ai.ansight:ansight-tools-filesystem-android`
- `ai.ansight:ansight-tools-preferences-android`
- `ai.ansight:ansight-tools-securestorage-android`
- `ai.ansight:ansight-tools-database-android`
- `ai.ansight:ansight-tools-reflection-android`
- `ai.ansight:ansight-android` is the all-in-one package that references core, pairing, and all standard tool suites.

Each tool suite exposes a `*ToolIds` constants object, matching the .NET `*ToolIds` pattern.

## Custom session properties

Apps can provide grouped string custom properties in `AnsightOptions.customProperties` before initialization, then update them while the runtime is active:

```kotlin
Ansight.initializeAndActivate(
    application = application,
    options = Ansight.developerOptions().copy(
        customProperties = mapOf("app" to mapOf("tenant" to "acme")),
    ),
)

Ansight.registerCustomProperty("app", "region", "au")
Ansight.removeCustomProperty("app", "tenant")
Ansight.clearCustomProperties()
```

When a live pairing session is connected, mutations are sent immediately with the `session.properties` control action. When disconnected, mutations are retained locally and included in the next `session.open`.

## Local publication

1. Create `local.properties` in this directory with your Android SDK path when
   `ANDROID_HOME` or `ANDROID_SDK_ROOT` is not already configured.
2. Publish the release AARs to your local Maven cache:

```bash
./gradlew publishReleasePublicationToMavenLocal
```

You can override the publication coordinates with Gradle properties:

```bash
./gradlew :ansight:publishReleasePublicationToMavenLocal \
  -PansightAndroidGroup=com.example \
  -PansightAndroidArtifactId=ansight-android \
  -PansightAndroidVersion=1.0.0-local
```

## Validation commands

```bash
./gradlew :ansight-core:testDebugUnitTest
./gradlew :ansight:testDebugUnitTest
./gradlew :harness:assembleDebug
./gradlew :ansight:publishReleasePublicationToMavenLocal
```
