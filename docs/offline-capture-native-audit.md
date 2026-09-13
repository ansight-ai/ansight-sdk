# Native offline capture audit

Audited 11 September 2026 against SDK commit `4389e5b`. This is a source audit, not an implementation or device validation. Scope: Android, Apple, Flutter, React Native, Capacitor, and migration of .NET mobile. The host/importer and hosted ingest service were not audited.

**Recommendation:** make Swift and Kotlin the owners of capture scheduling, record routing, disk persistence, retention, crash attachment, export, and upload. Keep .NET, Dart, and JavaScript as control facades and framework-specific data producers. Share an archive specification and compatibility fixtures between the two native implementations. A new C/C++ storage core would add another interoperability and build boundary; nothing in the current architecture requires it.

## Current implementation and gaps

| Area | Evidence | Implication |
| --- | --- | --- |
| Offline recording is .NET-only | `src/dotnet/Ansight.OfflineCapture/OfflineCaptureController.cs:135` starts the writer, metadata, queue, and screenshot pump. `docs/sdk-api-parity.md` lists no native offline SDK. | Native crash association methods are integration hooks, not complete offline recorders. |
| Native telemetry already exists | Swift `AnsightRuntime.swift:2121`; Kotlin `AnsightRuntime.kt:2305`. .NET `RuntimeImpl.cs:325` reads native snapshots and imports them into its managed data sink. | Attach persistence at native record ingestion, before buffer trimming. Avoid polling the live buffer through a framework bridge to implement offline capture. |
| Screenshot scheduling depends on live sessions | Swift `AnsightRuntime.swift:2530` requires `sessionOpen`; Kotlin `AnsightRuntime.kt:2665` requires an open transport. Both respect host-owned screenshot policy. | Offline demand must start capture independently. Disconnecting a host, or switching live capture to host mode, must not stop offline screenshots. |
| .NET offline screenshots use separate platform code | `OfflineCaptureController.cs:975` calls the managed screenshot pump; `SessionJpegCaptureSupport.Apple.cs` and `.Android.cs` implement rendering/encoding. | Native live and managed offline screenshot work can coexist. Migrate to a shared native scheduler before deleting the managed implementations. |
| Native network requests require a connection | Swift `AnsightRuntime.swift:1429`; Kotlin `AnsightRuntime.kt:805` return failure without an open host transport. | Sanitize and offer records to offline storage before considering live delivery; return ingestion status separately from transport status. |
| Framework network instrumentation also requires a host | Flutter `ansight_network.dart:18`; React Native `index.js:2509`; Capacitor `src/index.ts:326`. | Adding native persistence alone will still miss framework requests offline. Change hook activation to explicit network opt-in AND an eligible live or offline consumer. |
| .NET offline touch feed appears disconnected on native targets | `RuntimeImpl.cs:256` only starts the managed touch session when native runtime is unavailable; `OfflineCaptureController.cs:782` subscribes to that managed hub. `SyncNativeTelemetry` imports metrics/events, not touches. | Source-level likely regression: offline touch records can be absent on native-backed .NET. A device test must establish current behavior; direct native persistence removes this dependency. |
| Crash attachment still runs in .NET | `OfflineCaptureController.cs:1126` reads the native crash outbox; `:1180` writes crash files, seals the matching manifest, and acknowledges persistence. | Move this recovery transaction native, including process/session correlation and the separate host/offline delivery acknowledgments. |
| Visual-tree capture is not part of the current offline writer | The controller persists JPEGs and their index; it does not persist trees. Native live runtimes have screenshot/tree and touch-triggered tree capture. | Basic .NET parity does not imply offline tree parity. Define a versioned tree format and validate importer support before including it in the release scope. |

Paths in this table are repository-relative; Swift and Kotlin runtime references are under `src/ios/Sources/AnsightCore` and `src/android/ansight-core/src/main/kotlin/ai/ansight/runtime` respectively.

## Proposed ownership

```text
OS/native producers              Framework producers
metrics, touches, frames,        CLR/Dart/JS telemetry, HTTP hooks,
lifecycle, native network        MAUI routes, framework trees, feedback
             |                         |
             +------- native ingestion +
                           |
                  validated capture records
                     /             \
             live transport     offline recorder
                                bounded queue
                                segmented disk store
                                recovery + retention
                                export + upload
```

The ingestion boundary should accept typed records with stable IDs, source, original capture timestamps, and sequence numbers. It should take bounded work on the producer thread, with serialization and disk work outside runtime locks and the UI thread. Preserve 64-bit metric values across JavaScript-facing APIs without lossy number conversion.

One native capture coordinator should combine live and offline demand. Capture an eligible frame once where settings permit, then route it to independently bounded consumers. If consumers request different cadence, resolution, quality, or tree modes, explicitly reconcile settings and downsample/re-encode where necessary. Do not let a slow WebSocket block disk capture or a slow disk block the app. Live host screenshot policy applies only to live delivery.

The native recorder should expose initialize/configure, start, stop, update options, current status, retained-session listing, export, and upload. Preserve existing activation meanings (`Disabled`, `Immediate`, `NextSessionOnly`, `AlwaysOn`) and effective runtime defaults. Publish status changes so framework network hooks also react to persisted automatic activation. Status should distinguish recording, stopping, stopped, and failed, and expose accepted/written/dropped counts and the last persistence error.

Return export paths or platform file handles through bridges, not archive bytes or base64. Adapt the existing .NET stream-export API using a native export file and a bounded stream copy if necessary. Preserve asynchronous completion, cancellation, upload progress, and typed error codes.

Not every producer belongs in native code. CLR counters, Dart HTTP interception, JavaScript fetch/XHR interception, MAUI routing, React component information, and DOM/Flutter semantics need their originating runtime. Keep those hooks small and forward their output to native ingestion. Annotation UI and bundle creation can remain managed while native code owns durable bundle storage. Moving all framework instrumentation native would lose evidence or recreate framework-specific bridges inside the capture engine.

## Archive and behavior compatibility

Use the existing .NET archive as the initial compatibility contract:

- `.ansight/sessions/{sessionId}/manifest.json`, metadata, compact telemetry/touch JSONL segments, request documents, JPEG files and index, annotation bundles/index, and crash reports/traces.
- Preserve manifest property casing, enum representation, date/time and duration formats, optional-field behavior, channel IDs, event/touch IDs, coordinate units/scales, and ZIP entry paths. Default Swift/Kotlin encoders will not automatically reproduce the .NET contract.
- Preserve per-request `ansight.network-request.v1` documents and the current upload flow: create using the app-scoped API key, PUT the archive to the signed URL, then complete. Preserve idempotency, transient retries, cancellation, progress, and temporary-file cleanup. Do not forward the API key to object storage.
- Plain ZIP and password-protected AES-256 ZIP are distinct requirements. The current implementation uses `ZipArchive` and SharpZipLib respectively. Native archive dependencies and encrypted interoperability need a separate implementation spike; no native ZIP library has been selected in this audit.
- Native export must read previously recorded .NET sessions. Mobile migration must retain or discover the existing root directory and settings, instead of silently changing the default location and losing access to recordings.

Generate representative .NET archives as fixtures and verify both native writers against them semantically. Also import native archives through the actual host/CLI and hosted ingest before claiming compatibility. The SDK README's claim that host accepts the compact format is not a substitute for consumer testing.

**Network policy needs an explicit decision:** the offline README says bodies are omitted, but `RuntimeImpl.RecordNetworkRequest` uses `NetworkRequestSanitizer.SanitizeForTransport`, which preserves captured bodies (`NetworkRequestSanitizer.cs:110`), and the writer serializes the resulting request. Apple also supports toggling redaction, while Android's native record method unconditionally sanitizes. Establish a common persistence policy for bodies, redaction, and size limits; do not port the README assumption. Retain early framework sanitization and enforce the selected policy at native persistence too.

## Reliability work to include in the port

These are source-observed risks to address and test, not failures reproduced on a device during this audit.

1. **Serialize controller transitions.** Start currently checks state, awaits file preparation, and later publishes state. Concurrent start/stop/update/export need a native state machine with one writer owner and rollback after startup failure.
2. **Separate commands from droppable records.** The .NET queue uses `DropOldest`, and `DrainAndFlushWritersAsync` inserts a flush completion record into that same queue. Later traffic can evict it. Native flush/stop barriers must never be dropped, and writer failure must complete waiting operations with an error.
3. **Bound bytes as well as record counts.** A limit of 4,096 records does not bound memory when records contain bodies or large payloads. Give telemetry, frames, and durable annotation submissions explicit budgets. Report actual persisted counts, not only queue admissions.
4. **Retain related files together.** The current retention manager independently removes old closed files and protects settings/manifests but not all session metadata. A JPEG is written separately from its droppable index record. Preserve metadata required to interpret retained data and evict payload/index pairs or coherent segments together. Handle orphan files after interruption.
5. **Define a stable export boundary.** Current export drains writers then enumerates files while active capture may continue; retention can also remove files. Pin a sealed snapshot, rotate active segments, and prevent retention from deleting leased export/upload inputs. Upload already rejects active captures; preserve that behavior initially.
6. **Recover interrupted writes.** Use atomic metadata replacement, a defined flush policy, tolerant handling of a truncated final JSONL record, and safe recovery of unfinished manifests. A recovered crash must be acknowledged only after its files and manifest update succeed. Do not equate every unclean stop with a proven crash.
7. **Make lifecycle behavior explicit.** Suspend unavailable UI producers on backgrounding, bound flush work, and resume cleanly. Persisted activation is evaluated at native startup; it does not promise arbitrary background execution. Test process kill and immediate relaunch as well as ordinary stop.
8. **Do not assume byte limits are hard caps.** Protected active files can exceed current retention limits. Define rotation thresholds and behavior when disk is full or protected files alone exceed the budget. Surface degraded capture instead of remaining apparently healthy.

## Platform work

| Platform | Required work | Scope |
| --- | --- | --- |
| Android/Kotlin | Add recorder/store/retention/recovery; connect metrics/events/touches/network ingestion; separate screenshot production from transport; add export/upload; publish APIs/artifacts and device tests. | Large; one of the two core implementations. |
| iOS and Mac Catalyst/Swift | Same recorder responsibilities; reuse UIKit screenshot renderer and native crash machinery; lifecycle-aware scheduling; Swift/Objective-C facade; update .NET Apple bridge packaging. | Large; second core implementation. Catalyst requires its own validation. |
| .NET mobile / MAUI | Preserve `Ansight.OfflineCapture` public API while delegating to native; extend `INativeRuntimeBridge`, Android bridge/binding and Apple bridge/binding; migrate settings/paths; route feedback bundles; retain managed-only producers. Remove duplicate mobile capture after parity tests. | Medium to large; compatibility work, not just binding declarations. |
| Flutter | Add Pigeon API definitions and regenerate bridges; expose Dart options/status/session/export/upload APIs; update HTTP capture gating; forward framework records and validate rendered Flutter/GPU content. | Medium after native foundations. |
| React Native / Expo native builds | Add Swift/Objective-C and Kotlin methods, JS API and type declarations; update fetch/XHR hook lifecycle; keep React semantics and JS runtime instrumentation; validate ordinary RN and Expo development builds. | Medium after native foundations. |
| Capacitor mobile | Add Swift/Kotlin plugin methods and TypeScript API/status changes; update browser network hook lifecycle; retain DOM providers; use platform file export/share integration. | Medium after native foundations. |
| Native macOS/AppKit | Swift package declares macOS support, but `AnsightScreenCapture.swift` throws unavailable without UIKit. Storage/telemetry are plausible reuse; AppKit screenshot/touch/lifecycle evidence needs a separate audit and implementation. | Additional scope; do not claim full parity from package support. |
| Plain .NET / Windows / Linux | Existing `net9.0` offline target is portable; default screenshot implementation returns no frame. Keep the managed storage fallback unless deliberately dropping this target. | Retain existing capability; full native desktop capture is separate work. |
| Browser-only | No Swift/Kotlin runtime to delegate to. RN explicitly excludes Expo Web. | Separate browser storage/capture backend if requested; outside this native port. |

Prefer optional offline modules/products over forcing archive/upload dependencies into every live-only app. Keep the small ingestion/sink extension points in native core to avoid dependency cycles. Packaging must cover SwiftPM, the existing Apple framework/Objective-C surfaces, Android publication, native bindings, and each framework's dependency versions.

## Delivery sequence and effort

Planning estimate: **8–13 engineer-weeks** for both mobile native implementations, migration of .NET mobile, all three framework facades, export/upload parity, and release validation. This is a judgment estimate from source scope, not a measured schedule; allow revision after the first native prototype and encryption/importer spikes.

| Phase | Deliverable | Estimated effort |
| --- | --- | --- |
| 1 | Freeze archive/API behavior; fixtures; disconnected-capture reproduction; native sink/coordinator design; ZIP/encryption feasibility. | 1 week |
| 2 | Swift and Kotlin stores, producer routing, scheduling, activation, retention, crash recovery, plain/encrypted export and upload. | 4–6 engineer-weeks combined |
| 3 | .NET migration plus Flutter, RN and Capacitor APIs, status propagation and framework network hooks. | 2–3 engineer-weeks combined |
| 4 | Device, importer and upload validation; fault/load tests; packaging, samples and documentation. | 1–3 engineer-weeks |

This excludes new annotated-feedback UIs for other frameworks, offline visual-tree schema/importer development, native AppKit/Windows/Linux capture, browser-only recording, and durable OS-scheduled background uploads. Two platform engineers can overlap phase 2 after agreeing the contract; framework work can follow a stable API, but integration validation remains necessary.

The first useful milestone is a native Android and native Apple harness that can start without ever connecting to a host, capture telemetry/touches/network/JPEGs, survive interruption, and export an archive the existing consumer can read. Then migrate the .NET sample to prove that native ownership actually removes the managed capture path before rolling out the other facades.

## Acceptance criteria

- Start with no host, record real app interactions and framework HTTP traffic, stop, export, and inspect every stream in the consumer.
- Connect/disconnect and switch live screenshot ownership while recording; offline capture remains continuous and frame work does not duplicate unnecessarily.
- Test native UIKit/Android, MAUI, Flutter, RN/Expo native, and Capacitor on physical iOS/Android devices; include Catalyst and GPU/WebView content where applicable.
- Verify immediate, next-session-only and always-on activation, mutable limits/options, invalid active changes, and migration of existing .NET recordings/settings.
- Exercise overload, disk full, permission failure, concurrent lifecycle calls, cancellation, retention during export, truncated files, forced termination and crash recovery. Flush/stop must terminate deterministically.
- Compare native archives to .NET fixtures; verify plain and encrypted export, importer compatibility, upload retries/idempotency, and no API key on storage requests.
- Confirm managed/Dart/JS metrics appear once, touch coordinates match screenshots, and network bodies/redaction follow the agreed persistence policy.
- Measure FPS, UI-thread capture duration, CPU, memory, queue drops, bytes/minute and shutdown time with JPEG capture both enabled and disabled. Native centralization removes duplicated work; it does not remove screenshot rendering cost.

Existing `src/dotnet/tests/Ansight.UnitTests/OfflineCaptureTests.cs` covers basic recording, options, activation, export and upload behavior. It is a useful contract starting point, but does not establish the native/device acceptance criteria above. No tests were run for this documentation-only audit.
