# Ansight

Aggregate Swift product for Ansight developer integrations.

This product references `AnsightCore`, `AnsightPairingQR`, and all current
native iOS tool suites.

## Usage

```swift
#if DEBUG
import Ansight

try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
#endif
```

Start the local host with `ansight host run`. iOS Simulator and Mac Catalyst
register automatically through loopback; no explicit `.auto(...)` connection is
needed. For a physical device, run `ansight pairing issue --qr`, then present
`.qrCode(...)` from a developer-only app surface.

`initializeAndActivateAnsightSdk(...)` applies the aligned all-in-one defaults:
400 ms sampling, 120 second retention, FPS, UIKit lifecycle capture, touch
capture, 2000 ms JPEG capture at quality 60 and max width 480 with
GPU-backed surface capture enabled, host auto-probe, full tool access, platform
file/QR pairing, and all native tool suites.

> **Important:** Screen capture will result in an FPS drop while frames are
> rendered, encoded, and sent. Disable session JPEG capture when a run is
> measuring performance rather than collecting visual evidence.

Customize registered tool suites with `AnsightRemoteToolOptions`.

See the [iOS getting-started guide](https://www.ansight.ai/docs/sdk/ios/setup)
for guarded setup and CLI verification, or [../../README.md](../../README.md)
for the complete SDK reference.
