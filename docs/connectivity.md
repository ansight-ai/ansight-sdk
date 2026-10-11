# Connectivity capture

Connectivity records the OS view of the device's connection independently of HTTP request capture and the connection to the Ansight host. It is opt-in. It performs no pings, speed tests, DNS requests, Wi-Fi scans, or permission prompts, and collects no SSIDs, BSSIDs, IP addresses, carrier names, or network identifiers.

## Packages and setup

The portable record contract is in `Ansight.Protocol`. Collection is enabled by the optional `Ansight.Connectivity` package; the all-in-one `Ansight` package includes it. The core event transport and capture readers can carry connectivity without enabling a collector.

```csharp
using Ansight;
using Ansight.Connectivity;

var options = Options.CreateBuilder()
    .WithConnectivityCapture(capture => capture.MinimumSampleIntervalMilliseconds = 5_000)
    .Build();
Runtime.Initialize(options);
Runtime.Activate();
```

Add this to the same builder used to configure `Ansight.OfflineCapture`. The existing offline recorder writes observations without a host connection. Starting an offline capture includes the last known connectivity observation as context, preserving its original timestamp. Collection follows runtime activation and pauses with an explicit observation gap when a mobile app backgrounds. Foregrounding starts a new snapshot.

Native package names are `AnsightConnectivity` (SwiftPM/CocoaPods) and `ai.ansight:ansight-connectivity-android` (Gradle). Native collectors live alongside the native runtime so bridges observe the same default network path. Enable them with:

```swift
let options = try AnsightOptionsBuilder().withConnectivityCapture().build()
```

```kotlin
val options = AnsightOptionsBuilder().withConnectivityCapture().build()
```

React Native and Capacitor expose `createOptionsBuilder().withConnectivityCapture().build()`. Flutter exposes `AnsightOptionsBuilder().withConnectivityCapture().build()`. Their option is `connectivityCapture: { minimumSampleIntervalMilliseconds: 5000 }`; `withoutConnectivityCapture()` disables it. Enable [native offline capture](native-offline-capture-implementation.md) separately to retain those events while disconnected and transfer recordings to the host on reconnect. Native offline capture requires SDK 1.8.1 or later and the matching host receiver; it is not included in the 1.8.0 packages.

The quality-only interval is clamped to 1–300 seconds. State transitions are emitted immediately. Unsupported fields remain absent, not false or zero.

## Platform availability

| Measurement | Apple | Android | Portable .NET desktop |
| --- | --- | --- | --- |
| Availability and active path transports | `NWPathMonitor` | Default network callbacks; broadcast fallback on older OS versions | Availability only; default-route transport unknown |
| VPN | Unknown | OS transport flag | Unknown |
| Internet validation and captive portal | Unknown | OS capabilities on API 23+ | Unknown |
| Metered / expensive | Expensive flag | Metered capability | Unknown |
| Low Data Mode | Constrained flag | Unknown; Data Saver is not treated as equivalent | Unknown |
| Link quality | OS estimate on iOS/macOS 26+ | Unknown | Unknown |
| Signal and estimated bandwidth | Unknown | Capability values when exposed; signal API 29+ | Unknown |

Android signal values retain the `bearer-specific` unit because the capability API does not provide a universal scale. Bandwidth is stored in kbps. Link estimates are not throughput or latency measurements. “Available” means the OS has a usable connection; it does not prove an application server or the public internet is reachable.

## Capture contract

Canonical files are `connectivity/index.json` and `connectivity/segments/000001.jsonl`. The index uses `ansight.connectivity-index.v1`, lists segment time bounds, retained record count, supported measurements, retention status, and a compact state-change overview. Segments contain `ansight.connectivity.v1` observations:

- `id`, `observationId`, `sequence`, `capturedAtUtc`, and monotonic `elapsedMilliseconds` identify and order observations.
- `kind` is `snapshot`, `change`, `quality`, or `gap`.
- `snapshot` contains availability, transports, and optional VPN, validation, captive portal, metered, expensive, and Low Data Mode flags.
- A `change` also includes `previousSnapshot`.
- Optional `quality` includes its own `measuredAtUtc`, `source`, link quality, signal value and unit, and upstream/downstream estimates. Measurement age is derived at the playhead; it is not reset by loading or exporting a capture.
- `source`, `supportedMeasurements`, and optional `reason` describe provenance and observation gaps.

Each record contains complete connection state. Segments start with the preceding observation when one exists. Checkpoints preserve ID and timestamp and are deduplicated on read. Retention keeps a preceding state in the surviving index. Queue overflow produces a gap, and the offline manifest records dropped records. No connectivity index is written when no observations were captured.

Records also travel as `connectivity.*` application events for live transport and compatibility. Streamable exports omit that duplicate payload from `session.json` and retain the canonical segments. Raw offline archives and streamable archives import through the same host model. Existing archives without connectivity remain valid.

Extraction preserves the preceding state. Cutting a timeline rebases playback timestamps and saves source timestamps in `originalCapturedAtUtc` and `quality.originalMeasuredAtUtc`. A cut boundary carries the state at the end of the removed range.

## Playback and inspection

The shared local/cloud player provides a Connectivity timeline and tab. The playhead controls the current state, optional flags, source, and quality measurement age. Unknown/gap intervals are hatched; unavailable intervals have a distinct color. Clicking an interval, change, or quality point seeks the capture, so screenshots, logs, and annotations remain aligned. Quality charts separate sources, transports, and units and never interpolate missing samples. Cloud playback fetches the index and only the segments around the playhead, using the session's existing authenticated storage access.

```sh
ansight session connectivity <session-id> summary --json
ansight session connectivity <session-id> events --from 00:30 --to 02:00 --json
ansight session connectivity <session-id> quality --limit 100 --json
ansight session connectivity <session-id> at --offset 01:15 --json
ansight session connectivity <session-id> timeline --annotation <annotation-id> --before 120 --after 30 --json
```

Times accept ISO-8601 or `mm:ss` / `hh:mm:ss` session offsets. Queries report capture bounds, clipping, malformed record count, and preceding state. Timeline/quality/event pages return `nextCursor`; repeat the same filters with `--cursor`. If the live result changes, restart the query. Limits are 1–1,000 records and responses have a bounded payload size.

Agent tools are `ansight_get_connectivity_summary`, `ansight_get_connectivity_timeline`, and `ansight_get_connectivity_at`. They accept a live or imported `sessionId`, time windows, or `annotationId` plus `beforeSeconds` / `afterSeconds`. Use annotations to bracket a symptom, then correlate connectivity with network requests, logs, and screenshots. A connection observation alone does not establish the cause of a failed request.

## Network conditioning harness

The [MAUI network harness](../test-apps/core/NETWORK-CONDITIONING.md) includes a
local transfer fixture and a TypeScript conditioning/recovery task. Conditioning
runs outside the app through the Ansight host; `Ansight.Connectivity` remains a
passive capture extension.
