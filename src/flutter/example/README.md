# Flutter Harness

This directory contains a minimal Flutter harness app for exercising the local plugin API.

The Android host project lives under `android/` and expects the native Android runtime to be published first:

```sh
cd ../../android
./gradlew :ansight-runtime:publishReleasePublicationToMavenLocal
```

Then create `android/local.properties` from `android/local.properties.example` and run the harness:

```sh
cp android/local.properties.example android/local.properties
flutter pub get
flutter run
```

The current example is Android-hosted. Native iOS runtime validation continues to live in `../../ios/Examples/NativeHarness`.
