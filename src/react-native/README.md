# Ansight React Native

The React Native bridge plan lives in [IMPLEMENTATION_SPEC.md](IMPLEMENTATION_SPEC.md).

The current scaffold expects the host app to consume the published native Android runtime and the native iOS runtime from `src/ios`.

The bridge harness app lives in `example/`.

## Android consumption

Publish the Android runtime first from `src/android`:

```bash
./gradlew :ansight-runtime:publishReleasePublicationToMavenLocal
```

Then add `mavenLocal()` in the host Android project's repository list and let the bridge resolve:

```properties
ansightRuntimeCoordinates=ai.ansight:ansight-runtime-android:0.1.0-dev
```

`ansightRuntimeCoordinates` is optional if you keep the default coordinates.
