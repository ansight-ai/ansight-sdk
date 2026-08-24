# AnsightPairingQR

UIKit enrollment-invite import and AVFoundation QR scanning support for
runtime-owned host connections.

The aggregate `Ansight` product registers `PlatformHostConnectionConfigReader`
by default. Lower-level `AnsightCore` integrations can register it directly.

## Usage

With the local host running, issue a one-use QR:

```sh
ansight pairing issue --qr
```

Register the platform reader, then open the scanner only from a developer-only
app surface:

```swift
import AnsightCore
import AnsightPairingQR

AnsightRuntime.shared.setHostConnectionConfigReader(
    PlatformHostConnectionConfigReader()
)

await AnsightRuntime.shared.connect(.qrCode(title: "Scan Ansight Enrollment QR"))
```

Apps using QR scanning must include `NSCameraUsageDescription` in `Info.plist`.
If an approved workflow distributes the current one-use invite as a file, use
`.file(title: "Import Ansight Enrollment Invite")`; a bundled pairing file is
not part of normal setup.

See the [iOS enrollment guide](https://www.ansight.ai/docs/sdk/ios/pairing) for
the complete physical-device flow.
