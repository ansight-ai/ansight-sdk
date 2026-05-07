# Ansight Android

The native Android runtime plan lives in [IMPLEMENTATION_SPEC.md](IMPLEMENTATION_SPEC.md).

The native harness app lives in `harness/`.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Local publication

The Android runtime publishes as `ai.ansight:ansight-runtime-android:0.1.0-pre1` by default.

1. Create `local.properties` in this directory with your Android SDK path.
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
