# Native offline capture

Swift and Kotlin own disconnected recording, retention, recovery, ZIP export and
host delivery. No .NET process, timer, recorder or uploader is needed by this mobile
pipeline. .NET/MAUI, Flutter, React Native and Capacitor forward configuration and
framework evidence to the same native engines. The old managed offline API remains
available for compatibility; applications using native capture must stop creating
its parallel recorder.

## Policy and lifecycle

Native capture is opt-in. `withOfflineCapture()` (`WithOfflineCapture()` in .NET)
enables it with these defaults:

| Setting | Default | Behavior |
| --- | --- | --- |
| `maximumSessions` | **10** | Positive, configurable count including the active recording. |
| `transferMode` | `automatic` | Transfer on authenticated reconnect; `manual` waits for an explicit request. |
| `recordingEnabled` | `true` | Initial entitlement gate, independent of configuring the feature. |
| `maximumSessionBytes` | 128 MiB | Rotate when the next record would exceed the payload budget. |
| `maximumRetainedBytes` | 512 MiB | Evict oldest whole sessions to bound retained payload bytes. |
| `maximumQueuedBytes` | 8 MiB | Bound pending records and attachments before admitting producer work. |
| `rootDirectory` | App-private storage | Override to adopt an existing capture directory. |

Constructing raw native `OfflineCaptureOptions` leaves `enabled = false`; native
builders opt in. Set `enabled: true` when constructing a JSON policy directly.
Options are validated at initialization. Count/byte budgets and transfer mode take
effect at the next initialization. The entitlement gate can change while running.

An active, eligible runtime records while disconnected, including before its first
pairing. A host connection seals the current recording. Disconnecting creates a new
recording with the same process-session identity. Deactivation or entitlement
revocation seals recording and stops automatic transfers. Screenshots pause in the
background. Retention evicts oldest unleased sessions, including untransferred ones;
transfer leases protect in-flight sessions. A temporary lease conflict resumes once
the lease is released. Byte budgets cover payload files, not manifests, filesystem
overhead or the temporary ZIP used during delivery.

Each session contains its device profile, initial channel definitions, current custom
properties, metrics, events, client logs, touches, sanitized network records and,
when configured, screenshots and visual trees. Framework HTTP wrappers keep capture
active when native status reports `isOfflineCaptureRecording`. Native crash capture
tracks the offline session identity and retains its existing independent host handoff.
Visual trees are sampled with offline screenshots for either tree-enabled mode.

The native stores adopt the previous managed `sessions/<id>` JSONL layout, including
GUIDs without hyphens, and preserve additional manifest fields. Recovery seals
interrupted sessions and recounts payload bytes. Corrupt manifests fail visibly and
preserve evidence. This migration reads local session directories; it does not read
password-protected ZIPs or continue the previous hosted-cloud upload workflow.

## Configuration and manual transfer

.NET/MAUI:

```csharp
var options = Options.CreateBuilder()
    .WithOfflineCapture(new Ansight.Native.NativeOfflineCaptureOptions
    {
        MaximumSessions = 10, // Set any positive session count.
        TransferMode = Ansight.Native.OfflineCaptureTransferMode.Automatic,
        RecordingEnabled = false
    })
    .Build();
Runtime.Initialize(options);
await Runtime.SetOfflineCaptureEnabledAsync(userHasAnsightAccess);
Runtime.Activate();
// In manual mode, or to retry an earlier failure:
await Runtime.TransferOfflineCapturesAsync();
```

Swift: `AnsightOptionsBuilder.withOfflineCapture(OfflineCaptureOptions(enabled:
true, maximumSessions: 10))`. Kotlin:
`AnsightOptionsBuilder().withOfflineCapture(OfflineCaptureOptions(enabled = true,
maximumSessions = 10))`. Both runtimes expose `transferOfflineCaptures()`.

React Native and Capacitor: `.withOfflineCapture({ maximumSessions: 10,
transferMode: 'automatic' })`. Flutter: `.withOfflineCapture(const
AnsightOfflineCaptureOptions(maximumSessions: 10, transferMode: 'automatic'))`.

The native tool `offline_capture.transfer` is registered when capture is enabled.
Hosts can discover and call it through the existing app-tools API in manual mode.
The .NET tool facade routes that same operation through its native bridge. A
successful tool result means transfer started; completion is the imported session
on the host. `offlineCaptureLastError` on the native runtimes exposes failures.
Low-level `offline_capture.set_enabled` is intercepted locally by the native
control bridge and updates the entitlement gate without contacting a host.

## Delivery and deletion

The existing authenticated app WebSocket carries sequential transfers:

1. Flush/seal and lease the source session. Write a streaming deterministic ZIP and
   calculate its SHA-256; no full archive is buffered in memory on the app.
2. Send `offline_capture.begin` with schema `ansight.offline-transfer.v1`, source
   `sessionId`, 32-character `transferId`, `byteCount`, and lowercase `sha256`.
3. Send ASFT chunks (64 KiB), exact sequence/offset, then the completion frame.
4. Send `offline_capture.commit` with the same descriptor.
5. The host verifies length, digest, archive paths and app identity, imports and
   flushes evidence to disk, then returns a durable receipt with the source ID/hash.
6. Only a successful receipt with `durable: true` and the exact source ID/hash allows
   local deletion. Connection failure, older hosts and mismatched receipts retain
   the source for a later reconnect or manual retry.

The host persists source identity/hash on `offline-<source-session-id>`. A retry,
including after host restart or lost acknowledgement, returns the original import.
Import errors never authorize purge. Local-host delivery does not require a cloud
account or upload API key. The receiver caps archives at 1 GiB, uncompressed content
at 2 GiB and chunks at 1 MiB. Native default budgets fit those limits.

## Validation and rollout

Focused tests cover configurable counts, oldest/byte eviction, queue pressure,
metadata updates, screenshot indexes, legacy recovery, corrupt manifests, leases,
deterministic export, access gating, and automatic/manual/mismatched-receipt delivery.
Swift uses an injected transport; Android exercises real loopback WebSockets. Host
tests verify logs, events, metrics, touches, network, images and trees after persisted
import/restart, retry identity, invalid hashes, source/app mismatch and unsafe paths.

```sh
# Apple native recorder and sender
cd src/ios && swift test --filter OfflineCapture
# Android native recorder and sender
cd src/android && ./gradlew :ansight-core:testDebugUnitTest --tests 'ai.ansight.runtime.OfflineCapture*Test'
```

Also validated: .NET option serialization; Flutter offline HTTP/configuration;
React Native network/configuration; Capacitor options and TypeScript; native iOS
simulator/Android .NET bridges; Redpoint iOS simulator source integration.

Release the SDK and matching host together, then update app package references.
Physical-device launch, reconnect/flapping, process termination, disk-full behavior,
and all framework application builds still require release smoke coverage. Native
capture currently does not provide an offline annotated-feedback bundle sink or
attach crash-report files into these ZIPs; crash reports retain their native outbox
handoff. No device end-to-end run or package publication is claimed by these tests.
