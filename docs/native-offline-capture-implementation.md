# Native offline capture implementation

Status: native storage and ZIP export foundation implemented; the complete app-to-host
workflow is not yet implemented or enabled in Redpoint. See the
[native migration audit](offline-capture-native-audit.md) for the existing runtime gaps.

## Product contract

- Redpoint enables recording for users entitled to Ansight while its trusted host is
  disconnected. Loss of entitlement stops recording.
- The session cache limit is configurable. The default is **10 sessions**, including
  an active recording. This is a count limit, separate from byte limits.
- Transfer policy is configurable: `automatic` by default, or `manual`.
- On an authenticated host connection, seal the disconnected recording. Automatic
  mode transfers pending recordings; manual mode makes them available for a host
  action. Disconnecting starts a new recording with the same process-session ID.
- Delete a transferred recording only after a durable host import acknowledgement
  identifies the exact recording and archive hash. Retention eviction is a separate
  operation and may evict older, unacknowledged recordings to respect the cache budget.
- A lost connection or lost acknowledgement retains the source for retry. The host
  must recognize repeat imports instead of creating duplicate sessions.

## Native ownership

Swift owns Apple recording; Kotlin owns Android recording. Neither engine requires
.NET. Framework SDKs supply options and their own telemetry through small bridges.
Native code owns queues, disk writes, recovery, cache eviction, export, and transfer.

Keep CLR/Dart/JavaScript instrumentation in its originating framework and forward
its records to native ingestion. Screenshot scheduling, native touches, and network
ingestion must work before the first host connection. Mobile .NET must stop running
its parallel managed recorder after migration. Desktop managed fallback is a
separate compatibility concern.

## Implemented foundation

Apple sources are in `src/ios/Sources/AnsightCore/OfflineCapture`. Android sources
are in `src/android/ansight-core/src/main/kotlin/ai/ansight/runtime/offline`.

Both expose `OfflineCaptureOptions`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `enabled` | `false` | The app must opt in according to its user-access policy. |
| `maximumSessions` | `10` | Maximum retained session count, including the active session. |
| `maximumSessionBytes` | 128 MiB | Session payload budget; rotate when the next record exceeds it. |
| `maximumRetainedBytes` | 512 MiB | Payload budget across retained sessions. |
| `maximumQueuedBytes` | 8 MiB | Maximum queued record and attachment bytes. |
| `transferMode` | `automatic` | Policy value for the future connection coordinator. |

`maximumSessions` is not hard-coded in eviction. Applications can choose another
positive value. Invalid budgets fail validation. These are construction-time
options; runtime mutation and persistence of policy are not implemented yet.

The native stores provide ordered recording transitions, bounded record admission,
flush barriers that cannot be dropped, oldest-session eviction, sealed-session
leases, interrupted-session recovery, and acknowledgement-gated deletion. Payload
sizes are recalculated from disk for recovered sessions. Corrupt manifests fail
visibly and preserve the evidence instead of silently bypassing retention.

The byte budgets cover recorded payload, not directory/manifest overhead or export
temporary files. An admitted record can still fail during disk IO. `lastError` and
`flush()` expose failures; admission success is not a durability acknowledgement.
`droppedRecords` currently records cumulative drops for the store instance.

Export uses a streaming, uncompressed ZIP and SHA-256. The caller holds the session
lease until transfer finishes, releases it after failure, and cleans up the export
file. Identical sealed-session contents produce identical archives on the same
platform. Cross-platform binary identity is not required. Password-protected ZIP,
legacy .NET session migration, and hosted upload compatibility remain pending.

These stores are not attached to the native runtime yet. The transfer-mode value
does not itself start a network transfer. The deletion API is a low-level primitive:
only the trusted transfer coordinator may supply validated host receipts.

## Remaining implementation sequence

### 1. Native runtime ingestion and lifecycle

Add the options to native runtime builders and their JSON contracts. A coordinator
must serialize eligibility changes, activation, disconnect, reconnect, and shutdown.
Connect native metrics/events before live-buffer trimming, native touch ingestion,
sanitized network ingestion before its live-transport check, screenshots, and crash
recovery. Persist device profiles, channels, and custom properties with each session.

Separate screenshot demand from host transport. Reuse native capture providers so
Flutter and other framework surfaces remain available offline. Pause unavailable
UI producers in the background. Retain original capture timestamps and stable IDs.
Store screenshot/index pairs and annotation bundles coherently.

Prove this first in the native Apple and Android harnesses without ever pairing to a
host. Storage unit tests alone do not prove that native app evidence is captured.

### 2. Host transfer and durable receipts

Use the existing authenticated connection and ASFT binary transfer protocol. Add a
versioned offline-transfer descriptor containing origin/session ID, transfer ID,
archive length, and SHA-256. Validate limits and identifiers before creating a
receiver. Keep automatic transfers sequential and cancellable.

The host validates length and digest, imports through its offline archive reader,
durably saves all evidence and source identity, then acknowledges. Persist import
identity with the imported session so a host restart or lost acknowledgement does
not cause duplicate imports. A socket send or completed binary transfer is not an
import acknowledgement. Manual and automatic transfer use the same implementation.

A host without this capability leaves captures on the device. Cleanup removes
partial transfer files and expired leases without purging the recording. Verify
native archives with the actual host importer, including screenshots, touches,
network records, annotations, and crash evidence.

### 3. Framework facades and migration

Expose matching configuration, status, retained-session listing, transfer, and
export APIs through .NET/MAUI, Flutter, React Native, and Capacitor. Network hooks
must run when a configured offline consumer exists, independently of host state.
Keep 64-bit values intact across JavaScript bridges.

Migrate existing .NET storage and activation settings before switching writers;
do not let the native foundation interpret old manifests as its new internal store
format. Preserve old recordings, export/upload behavior, and annotation routing.
Update Android bindings, the Apple .NET framework project, package exports, and
API documentation together.

### 4. Redpoint integration

Map the existing Ansight access policy to native capture enablement. Configure
`maximumSessions = 10` and `transferMode = automatic` through the public SDK API,
with both values configurable. Replace the next-session-only Diagnostics control
with the current policy and status. Remove app-owned session scanning and cloud
upload receipts from the local-host transfer flow once the SDK owns that lifecycle.
Keep Redpoint's live 120-second telemetry window independent from offline retention.

## Validation

The foundation has matching Swift and Kotlin tests for default options, configurable
session limits, oldest-session eviction, byte retention, oversize admission, flush
barriers, interrupted recording recovery, transfer leases, mismatched receipts,
corrupt manifests, and deterministic ZIP export. Standard ZIP readers verify export.

Commands:

```sh
cd src/ios
swift test --filter OfflineCaptureStoreTests
```

```sh
cd src/android
./gradlew :ansight-core:testDebugUnitTest --tests ai.ansight.runtime.OfflineCaptureStoreTest
```

Before release, also test offline launch and real interactions, connection flapping,
manual/automatic modes, runtime cache-limit changes, revoked access, interrupted
transfers, duplicate retries, disk full, process termination, SDK migration, and
host restart on physical Apple and Android devices. Verify Redpoint produces no
duplicate managed/native recordings.
