# Ansight

Aggregate Swift product for Ansight developer integrations.

This product references `AnsightCore`, `AnsightPairingQR`, and all current
native iOS tool suites.

## Usage

```swift
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
await AnsightRuntime.shared.connect(.auto(clientName: "iOS App"))
```

`initializeAndActivateAnsightSdk(...)` applies the aligned all-in-one defaults:
400 ms sampling, 120 second retention, FPS, UIKit lifecycle capture, touch
capture, 2000 ms JPEG capture at quality 60 and max width 480, host auto-probe,
full tool access, platform file/QR pairing, and all native tool suites.

Customize registered tool suites with `AnsightRemoteToolOptions`.

See [../../README.md](../../README.md) for the full iOS guide.
