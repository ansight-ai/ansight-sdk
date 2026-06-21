# SDK Publishing

This repo ships four package surfaces:

- .NET / MAUI packages to NuGet.
- Android AARs to a Maven repository, usually Maven Central.
- iOS SwiftPM through Git tags, with optional CocoaPods specs.
- React Native to npm.

Run publishing from a committed release branch or tag. Public package registries
generally do not allow replacing a published version, so version alignment is
the first release gate.

## Versioning

Check current package metadata:

```bash
scripts/check-sdk-versions.sh
```

Set a new version across package metadata:

```bash
scripts/set-sdk-version.sh 0.2.0-preview.2
```

The version script updates:

- `src/dotnet/Directory.Build.props`
- `src/android/gradle.properties`
- native iOS podspec versions
- `src/react-native/package.json`
- the React Native Android dependency and README version references

## Dry Run

Prepare all artifacts without publishing:

```bash
scripts/publish-all-sdks.sh
```

The all-SDK script stops if versions are not aligned. By default it publishes
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
`--android-central` or `--android-remote`. It creates and pushes the
SwiftPM release tag, publishes NuGet packages, uploads Android artifacts to the
Sonatype Central Portal, pushes CocoaPods specs, and publishes the React Native
npm package.

Use `--skip-dotnet`, `--skip-android`, `--skip-ios-swiftpm`,
`--skip-ios-cocoapods`, or `--skip-react-native` to publish one surface at a
time after a failed partial release.

## Credentials

Publish scripts automatically load ignored local credentials from:

```bash
.env.publishing.local
```

NuGet:

```bash
export ANSIGHT_NUGET_API_KEY=...
export NUGET_SOURCE=https://api.nuget.org/v3/index.json
```

Android / Maven Central:

```bash
export ANSIGHT_GPG_SIGNING_KEY="$(cat private-key.asc)"
export ANSIGHT_GPG_SIGNING_PASSWORD=...
export SONATYPE_CENTRAL_USERNAME=...
export SONATYPE_CENTRAL_PASSWORD=...
```

For a private Maven repository instead of Central:

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

For a private specs repo:

```bash
export ANSIGHT_COCOAPODS_REPO=ansight-specs
scripts/publish-ios-cocoapods.sh --push
```

npm:

```bash
npm login
scripts/publish-react-native.sh --publish
```

or configure `NPM_TOKEN` in CI.

## Platform Notes

NuGet publishing uses `dotnet nuget push` through the existing
`src/dotnet/upload-nugets.sh` wrapper.

Android uses Gradle `maven-publish` for local/private Maven publication. Maven
Central publication is a two-step flow: publish signed artifacts into a local
Maven repository directory, then upload that directory as one Central Portal
bundle.

SwiftPM publication is just the pushed Git tag. The package must be buildable
at that tag.

CocoaPods uses generated release podspecs because the checked-in podspecs are
optimized for local `:path` development under `src/ios`. Generated specs point
at the repository Git tag and prefix source paths with `src/ios/`.

React Native publishes only the JS/native bridge package to npm. The package
expects the matching native Maven and CocoaPods versions to be available before
app consumers install it.

## References

- [NuGet package publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package)
- [dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push)
- [Gradle Maven Publish Plugin](https://docs.gradle.org/current/userguide/publishing_maven.html)
- [Sonatype Central Portal API](https://central.sonatype.org/publish/publish-portal-api/)
- [CocoaPods Trunk setup](https://guides.cocoapods.org/making/getting-setup-with-trunk.html)
- [npm scoped public package publishing](https://docs.npmjs.com/creating-and-publishing-scoped-public-packages/)
