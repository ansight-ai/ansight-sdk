# Publishing SDK releases

Ansight SDK versions are coordinated across .NET, Android, SwiftPM,
CocoaPods, React Native, Capacitor, and Flutter. Release all surfaces at the
same version so framework packages never point at an unpublished native
dependency.

## Prepare and validate

Start from an up-to-date `main` branch with publishing credentials available
through the shell or `.env.publishing.local`:

```sh
./scripts/set-sdk-version.sh <version>
./scripts/publish-all-sdks.sh --android-central
```

Promote each package changelog's `Unreleased` section to the release version,
review the generated metadata, and commit the complete release before tagging.
The rehearsal validates SwiftPM and native tests, builds and packs NuGet,
creates a signed Maven Central bundle without uploading it, generates release
podspecs, checks both npm packages, and runs the Flutter/pub.dev dry-run.

## Publish

Publish only from a clean, pushed release commit:

```sh
ANSIGHT_POD_SOURCE_TAG=v<version> \
  ./scripts/publish-all-sdks.sh --publish --android-central
```

This pushes the SwiftPM tag first, then publishes NuGet and Android native
artifacts before CocoaPods, React Native, Capacitor, and Flutter wrappers.
Explicit `ANSIGHT_POD_SOURCE_TAG` values take precedence over saved publishing
defaults so a stale local tag cannot silently target the wrong release.

Maven Central uses a user-managed deployment by default. Complete the release
in the Central Portal after the uploaded deployment validates, unless
`SONATYPE_CENTRAL_PUBLISHING_TYPE=AUTOMATIC` is intentionally configured.

## Verify public registries

Registry indexing is asynchronous. Retry the read-only verification until all
surfaces are visible:

```sh
./scripts/validate-published-sdk-packages.sh --version <version>
```

The validator checks NuGet, Maven Central, npm, pub.dev, CocoaPods, the current
SwiftPM manifest, and the release tag.
