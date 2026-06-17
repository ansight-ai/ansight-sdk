# ansight-pairing-android

Native Android pairing UI for runtime-owned Ansight host connections.

The all-in-one `ai.ansight:ansight-android` package includes this package.
Apps that depend only on `ansight-core-android` can add it when they need
SDK-owned QR/paste pairing UI.

## Usage

```kotlin
import ai.ansight.pairing.AnsightPairing

AnsightPairing.showPairingSheet(
    activity = activity,
    clientName = "Android App",
    expectedAppId = activity.packageName,
    onResult = { result ->
        // HostConnectionResult
    },
)
```

The pairing sheet returns into the same runtime-owned connection path used by
`AnsightRuntime.connect(...)`, so saved config, status, telemetry, and live tool
handling stay coordinated by the runtime.
