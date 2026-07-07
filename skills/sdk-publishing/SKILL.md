---
name: sdk-publishing
description: >-
  Publish Ansight SDK package releases. Use when preparing, dry-running, or
  publishing SDK versions across NuGet, Android Maven/Central, iOS
  SwiftPM/CocoaPods, and React Native npm packages; when checking version
  alignment; or when recovering from partial SDK release failures.
---

# SDK Publishing

Use this workflow to release the Ansight SDK package surfaces. Run commands from
the repository root unless a command explicitly changes directory.

## Release Surfaces

The repository publishes:

- .NET and MAUI packages to NuGet.
- Android AARs to Maven, usually Maven Central.
- iOS SwiftPM through Git tags, with optional CocoaPods specs.
- React Native to npm.

Run publishing from a committed release branch or tag. Public package registries
generally do not allow replacing an already-published version, so version
alignment is the first release gate.

## Version Gate

Check current package metadata:

```bash
scripts/check-sdk-versions.sh
```

Set a new version across package metadata:

```bash
scripts/set-sdk-version.sh <version>
```

The version script updates:

- `src/dotnet/Directory.Build.props`
- `src/android/gradle.properties`
- Android Gradle module fallback versions and Android validation defaults
- native iOS podspec versions
- iOS runtime SDK metadata reported to Ansight Studio
- `src/react-native/package.json`
- the React Native Android dependency and README version references

Run `scripts/check-sdk-versions.sh` again after version changes. It verifies
package metadata and runtime-reported SDK metadata.

## Dry Run

Prepare all artifacts without publishing:

```bash
scripts/publish-all-sdks.sh
```

The all-SDK script stops when versions are not aligned. By default it publishes
Android to Maven local, generates release CocoaPods podspecs under
`build/cocoapods-release-specs`, packs NuGet packages, validates SwiftPM, and
runs the React Native npm dry-run pack.

To prepare a signed Android Central Portal bundle during a dry run:

```bash
scripts/publish-all-sdks.sh --android-central
```

## Publish

The intended release flow is:

```bash
scripts/set-sdk-version.sh <version>
git diff
git commit -am "Release <version>"
scripts/publish-all-sdks.sh --publish --android-central
```

The publish command requires a clean worktree and an explicit Android target:
`--android-central` or `--android-remote`. It creates and pushes the SwiftPM
release tag, publishes NuGet packages, uploads Android artifacts to the
Sonatype Central Portal, pushes CocoaPods specs, and publishes the React Native
npm package.

Use these flags to publish one surface at a time after a failed partial release:

- `--skip-dotnet`
- `--skip-android`
- `--skip-ios-swiftpm`
- `--skip-ios-cocoapods`
- `--skip-react-native`

## Credentials

Publishing scripts automatically load ignored local credentials from:

```bash
.env.publishing.local
```

NuGet:

```bash
export ANSIGHT_NUGET_API_KEY=...
export NUGET_SOURCE=https://api.nuget.org/v3/index.json
```

Android and Maven Central:

```bash
export ANSIGHT_GPG_SIGNING_KEY="$(cat private-key.asc)"
export ANSIGHT_GPG_SIGNING_PASSWORD=...
export SONATYPE_CENTRAL_USERNAME=...
export SONATYPE_CENTRAL_PASSWORD=...
```

Private Maven repository:

```bash
export ANSIGHT_MAVEN_URL=https://maven.example.com/releases
export ANSIGHT_MAVEN_USERNAME=...
export ANSIGHT_MAVEN_PASSWORD=...
scripts/publish-android.sh --publish
```

CocoaPods:

```bash
pod trunk register dev@ansight.ai "Ansight"
export ANSIGHT_POD_SOURCE_GIT=https://github.com/ansight-ai/ansight-sdk.git
export ANSIGHT_POD_SOURCE_TAG=v<version>
```

Private CocoaPods specs repository:

```bash
export ANSIGHT_COCOAPODS_REPO=ansight-specs
scripts/publish-ios-cocoapods.sh --push
```

npm:

```bash
npm login
scripts/publish-react-native.sh --publish
```

CI may use `NPM_TOKEN` instead of `npm login`.

## Platform Notes

NuGet publishing uses `dotnet nuget push` through
`src/dotnet/upload-nugets.sh`.

Android uses Gradle `maven-publish` for local or private Maven publication.
Maven Central publication is a two-step flow: publish signed artifacts into a
local Maven repository directory, then upload that directory as one Central
Portal bundle.

SwiftPM publication is the pushed Git tag. The package must be buildable at
that tag.

CocoaPods uses generated release podspecs because the checked-in podspecs are
optimized for local `:path` development under `src/ios`. Generated specs point
at the repository Git tag and prefix source paths with `src/ios/`.

React Native publishes only the JavaScript/native bridge package to npm. The
package expects matching native Maven and CocoaPods versions to be available
before app consumers install it.

## Post-Publish Validation

After publishing, invoke the `sdk-package-validation` skill and validate package
availability plus harness consumption:

```bash
scripts/validate-published-sdk-packages.sh --version <version>
```
