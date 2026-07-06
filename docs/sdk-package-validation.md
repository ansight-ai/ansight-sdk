# SDK Package Validation

Use these checks after publishing to prove that consumers can install the SDKs
from the public package registries, and to flip the first-party harnesses between
local checkout dependencies and published packages.

Unless a command starts with `cd`, run it from the repository root.

## Registry Availability

Run the public registry check:

```bash
scripts/validate-published-sdk-packages.sh --version 1.0.2-preview.2
```

This checks Maven Central, npm, CocoaPods trunk, the current checkout's root
SwiftPM manifest, and the release tag's root `Package.swift`.

The `1.0.2-preview.1` tag was created before the repository root
`Package.swift` existed, so SwiftPM Git consumers cannot resolve that tag from
the repository URL. CocoaPods is the published iOS package path for that version.
Tags created after this change should pass the SwiftPM tag check.

## Android Native Harness

Local SDK modules:

```bash
cd src/android
./gradlew :harness:assembleDebug
```

Published Maven Central package:

```bash
cd src/android
./gradlew :harness:assembleDebug \
  -PansightHarnessDependencyMode=published \
  -PansightHarnessVersion=1.0.2-preview.2 \
  --refresh-dependencies
```

You can also use environment variables:

```bash
ANSIGHT_HARNESS_DEPENDENCY_MODE=published \
ANSIGHT_HARNESS_VERSION=1.0.2-preview.2 \
./gradlew :harness:assembleDebug --refresh-dependencies
```

## iOS Native Harness

Local SwiftPM package:

```bash
cd src/ios/Examples/NativeHarness
xcodegen generate --spec project.yml
open AnsightNativeHarness.xcodeproj
```

Published SwiftPM package:

```bash
cd src/ios/Examples/NativeHarness
xcodegen generate --spec project.published.yml
open AnsightNativeHarnessPublished.xcodeproj
```

The published project uses the GitHub package URL and the version pinned in
`project.published.yml`. `scripts/set-sdk-version.sh` keeps that version aligned
with the rest of the SDK metadata.

## React Native Harness

The first-party Expo harness lives at:

```text
../ansight-sdk-test-apps/react-native/ansight-react-native-harness
```

Configure it for the local SDK checkout:

```bash
scripts/configure-react-native-harness-source.sh --source local --install
```

For local Android native validation through the RN bridge, publish the Android
AARs to Maven local first:

```bash
(cd src/android && ./gradlew publishReleasePublicationToMavenLocal)
```

Configure the RN harness for the published npm, CocoaPods, and Maven packages:

```bash
scripts/configure-react-native-harness-source.sh \
  --source published \
  --version 1.0.2-preview.2 \
  --install
```

Then run the harness checks:

```bash
cd ../ansight-sdk-test-apps/react-native/ansight-react-native-harness
npm run typecheck
ANSIGHT_RN_HARNESS_PACKAGE_SOURCE=published npm run ios
ANSIGHT_RN_HARNESS_PACKAGE_SOURCE=published npm run android
```

The switcher updates `package.json`, the iOS `Podfile`, and Android repository
resolution. In published mode Android disables `mavenLocal()` so the harness
cannot accidentally validate a locally published AAR with the same version.
