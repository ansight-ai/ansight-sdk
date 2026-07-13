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

## Secure pairing

Protocol v2 uses a secret-free signed UDP offer, an exactly pinned `wss://`
connection, and a per-install P-256 client key. Android 6.0 (API 23) or newer is
required because the client private key is generated as a non-exportable
Android Keystore key. Secure v2 pairing fails closed on older releases.

Insecure protocol v1 is disabled by default. A development-only v1 connection
must opt in explicitly with
`AnsightOptionsBuilder().withInsecureV1PairingCompatibility()`. V1 sessions are
locally restricted to non-critical read tools and are never used as a fallback
after a v2 config or v2 connection failure.

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
- `snapshot`, `hostConnectionStatus`, `recordedMetrics`, `recordedEvents`

See [../../../docs/sdk-api-parity.md](../../../docs/sdk-api-parity.md) for the
cross-SDK API map.
