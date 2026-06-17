# AnsightCore

Core Swift runtime product for Ansight telemetry, host connection, live session
transport, tool registration, and protocol handling.

Use `AnsightCore` when an app wants to compose only the surfaces it needs. Use
the aggregate `Ansight` product for the all-in-one developer preset.

## Usage

```swift
import AnsightCore

try AnsightRuntime.shared.initializeAndActivate(
    options: AnsightOptions(
        sampleFrequencyMilliseconds: 500,
        retentionPeriodSeconds: 600,
        toolGuard: .readOnly
    )
)
```

Core defaults keep remote tools disabled. Register tool products explicitly and
set `toolGuard` to allow discovery/execution.

## Main APIs

- `initialize`, `initializeAndActivate`, `activate`, `deactivate`, `clear`
- `metric`, `event`, `screenViewed`, `setAppLifecycleState`
- `registerMetricChannel`, `registerMetricStream`
- `connect`, `disconnect`, `savePairingConfig`, `clearSavedPairing`, `clearCachedSession`
- `sendClientLog`, `updateSessionProperties`
- `registerTool`, `isToolRegistered`, `handleToolProtocolMessage`
- `snapshot`, `hostConnectionStatus`, `currentOptions`, `recordedMetrics`, `recordedEvents`

See [../../../../docs/sdk-api-parity.md](../../../../docs/sdk-api-parity.md)
for the cross-SDK API map.
