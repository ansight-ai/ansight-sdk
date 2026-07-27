# ansight-core-android

Core Android runtime package for Ansight telemetry, host connection, live
session transport, tool registration, and protocol handling.

Use this package when an app wants to compose only the surfaces it needs. Use
`ai.ansight:ansight-android` for the all-in-one developer preset.

## Usage

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime
import android.app.Application

class MyApplication : Application() {
    override fun onCreate() {
        super.onCreate()

        AnsightRuntime.initializeAndActivate(
            application = this,
            options = AnsightOptions(),
        )
    }
}
```

Core defaults keep remote tools disabled. Register tool packages explicitly and
set `toolGuard` to allow discovery/execution.

Session JPEG capture is configured with `AnsightSessionJpegCaptureOptions`.
`captureGpuBackedSurfaces` is accepted for cross-platform configuration parity
and defaults to `true`; the capture-mode tradeoff is currently meaningful on
iOS.

> **Important:** Screen capture will result in an FPS drop while frames are
> captured, encoded, and sent. Disable session JPEG capture for
> performance-focused runs unless visual evidence is required.

## Main APIs

- `AnsightRuntime.initialize`, `initializeAndActivate`, `activate`, `deactivate`, `clear`
- `metric`, `event`, `screenViewed`, `setAppLifecycleState`
- `registerMetricChannel`, `registerMetricStream`
- `connect`, `disconnect`, `savePairingConfig`, `clearSavedPairingConfig`
- `sendClientLog`, `updateCustomProperties`
- `registerTool`, `isToolRegistered`
- `addArtifactProvider`, `artifacts.query`, `artifacts.request`
- `snapshot`, `hostConnectionStatus`, `recordedMetrics`, `recordedEvents`

See [../../../docs/sdk-api-parity.md](../../../docs/sdk-api-parity.md) for the
cross-SDK API map.
