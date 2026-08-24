# ansight-android

All-in-one Android package for Ansight developer integrations.

This package references `ansight-core-android`, `ansight-pairing-android`, and
all current standard Android tool suites.

## Usage

```kotlin
import ai.ansight.Ansight

if (BuildConfig.DEBUG) {
    Ansight.initializeAndActivateDeveloperMode(
        application = application,
        clientName = "Android App",
    )
}
```

Start the local host with `ansight host run`. Emulators register automatically
through loopback. No account, pairing build constant, config file, host address,
or camera permission is required.

For a physical device, run `ansight pairing issue --qr`, then call
`Ansight.enrollFromQrCode(activity)` from a developer-only app surface. The scan
stores this app installation's registration for automatic reconnect.

`Ansight.developerOptions(...)` applies the aligned all-in-one defaults:
400 ms sampling, 120 second retention, FPS, touch capture, 2000 ms JPEG capture
at quality 60 and max width 480, host auto-probe, full tool access, and all
standard native tools.

> **Important:** Screen capture will result in an FPS drop while frames are
> captured, encoded, and sent. Disable session JPEG capture for
> performance-focused runs unless visual evidence is required.

See the [Android getting-started guide](https://www.ansight.ai/docs/sdk/android/setup)
for package setup and CLI verification, or [../README.md](../README.md) for the
complete SDK reference.
