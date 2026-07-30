# ansight-android

All-in-one Android package for Ansight developer integrations.

This package references `ansight-core-android`, `ansight-pairing-android`, and
all current standard Android tool suites.

## Usage

```kotlin
import ai.ansight.Ansight

Ansight.initializeAndActivate(application)
Ansight.enrollFromQrCode(activity)
```

No pairing build constant, config file, host address, or camera permission is
required. The scan stores this app installation's registration for automatic
reconnect.

`Ansight.developerOptions(...)` applies the aligned all-in-one defaults:
400 ms sampling, 120 second retention, FPS, touch capture, 2000 ms JPEG capture
at quality 60 and max width 480, host auto-probe, full tool access, and all
standard native tools.

> **Important:** Screen capture will result in an FPS drop while frames are
> captured, encoded, and sent. Disable session JPEG capture for
> performance-focused runs unless visual evidence is required.

See [../README.md](../README.md) for the full Android guide.
