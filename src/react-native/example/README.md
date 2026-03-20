# React Native Harness

This directory contains a minimal React Native harness app for exercising the local bridge API.

The Android host project lives under `android/` and expects the native Android runtime to be published first:

```sh
cd ../../android
./gradlew :ansight-runtime:publishReleasePublicationToMavenLocal
```

Then create `android/local.properties` from `android/local.properties.example`, install the example dependencies, start Metro, and run the harness:

```sh
cp android/local.properties.example android/local.properties
npm install
npm start
```

In a second terminal:

```sh
npm run android
```

The current example is Android-hosted. Native iOS runtime validation continues to live in `../../ios/Examples/NativeHarness`.
