# AnsightPairingQR

UIKit document-import and AVFoundation QR scanning support for runtime-owned
host connections.

The aggregate `Ansight` product registers `PlatformHostConnectionConfigReader`
by default. Lower-level `AnsightCore` integrations can register it directly.

## Usage

```swift
import AnsightCore
import AnsightPairingQR

AnsightRuntime.shared.setHostConnectionConfigReader(
    PlatformHostConnectionConfigReader()
)

await AnsightRuntime.shared.connect(.qrCode(title: "Scan Pairing QR"))
await AnsightRuntime.shared.connect(.file(title: "Import Pairing Config"))
```

Apps using QR scanning must include `NSCameraUsageDescription` in `Info.plist`.
