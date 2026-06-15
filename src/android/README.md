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

## Local publication

The Android runtime publishes as `ai.ansight:ansight-runtime-android:0.1.0-pre1` by default.

1. Create `local.properties` in this directory with your Android SDK path when
   `ANDROID_HOME` or `ANDROID_SDK_ROOT` is not already configured.
2. Publish the release AAR to your local Maven cache:

```bash
./gradlew :ansight-runtime:publishReleasePublicationToMavenLocal
```

You can override the publication coordinates with Gradle properties:

```bash
./gradlew :ansight-runtime:publishReleasePublicationToMavenLocal \
  -PansightAndroidGroup=com.example \
  -PansightAndroidArtifactId=ansight-runtime-android \
  -PansightAndroidVersion=1.0.0-local
```

## Validation commands

```bash
./gradlew :ansight-runtime:testDebugUnitTest
./gradlew :harness:assembleDebug
./gradlew :ansight-runtime:publishReleasePublicationToMavenLocal
```
