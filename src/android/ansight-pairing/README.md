# ansight-pairing-android

Native Android enrollment UI for runtime-owned Ansight host connections.

The all-in-one `ai.ansight:ansight-android` package includes this package.
Apps that depend only on `ansight-core-android` can add it when they need
SDK-owned QR or paste enrollment UI.

## Usage

With the local host running, issue a one-use QR:

```sh
ansight pairing issue --qr
```

Open the sheet only from a developer-only app surface:

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

The enrollment sheet returns into the same runtime-owned connection path used
by `AnsightRuntime.connect(...)`, so saved registration, status, telemetry, and
live tool handling stay coordinated by the runtime. No prior app registration
or app-specific invite is required.

See the [Android enrollment guide](https://www.ansight.ai/docs/sdk/android/pairing)
for the complete physical-device flow.
