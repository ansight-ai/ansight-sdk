# Ansight Android Runtime Implementation Spec

This document defines the native Android Ansight runtime. It is the Android protocol implementation and the Android platform package that other bridges consume.

Scope:

- native Android apps must be able to consume this directly as a Kotlin package
- React Native and Flutter must consume this runtime instead of reimplementing the protocol in JS or Dart
- `.NET` remains the current protocol reference implementation, but is out of scope for this platform plan

## Role in the architecture

The Android runtime is responsible for:

- local telemetry capture and retention
- pairing document parsing and signature validation
- UDP `CONNECT_REQ` / `CONNECT_RESP` flow
- WebSocket session transport and ack ordering
- baseline `DeviceAppProfile`
- telemetry streaming
- binary screenshot streaming
- built-in remote tools and tool guard enforcement

This runtime is the Android source of truth. React Native and Flutter should only bridge into it.

## Package shape

Primary distribution:

- Android library published as an AAR-backed Kotlin package
- consumable from native Android apps through Gradle/Maven
- consumable by `src/react-native` and `src/flutter` bridge layers

Recommended artifact shape:

- Gradle module under `src/android`
- published Maven coordinate later
- public Kotlin-first API, with Java interop where practical

Recommended repo shape:

- `src/android` contains the runtime
- `src/react-native` depends on this runtime
- `src/flutter` depends on this runtime

## Source of truth

Wire behavior should match:

- `docs/protocol/README.md`
- `docs/protocol/tools.md`
- `src/dotnet/Ansight/Pairing/...`
- `src/dotnet/Ansight/Telemetry/...`
- `src/dotnet/Ansight/Screenshot/...`
- `/Users/matthewrobbins/Development/git/ansight/ansight.studio/Ansight.Host/Discovery/UdpPairingServer.cs`
- `/Users/matthewrobbins/Development/git/ansight/ansight.studio/Ansight.Host/Runtime/WebSocketSessionManager.cs`

## Protocol contract to match

### Pairing documents

The Android runtime must accept:

- `ansight.pairing-config.v1`
- `ansight.pairing-bootstrap.v1`
- `ansight.pairing-connection-hint.v1`
- `ansight.discovery-hint.v1`
- `ansight.qr-pairing-connection.v1`

Validation rules:

- verify config signature using the host public key
- accept the same historical canonical JSON variants as `.NET`
- reject expired configs
- optionally reject mismatched `appId`

Bootstrap detail:

- if `connectionHint` exists, use its `configId`, `issuedAt`, `expiresAt`, `oneTimeToken`, and `challenge` in the effective config
- still verify the signature against the original `pairingConfig`

### Session connect flow

Current interoperable flow:

1. Parse and validate pairing document.
2. Resolve manual host IP.
3. Send UDP `CONNECT_REQ` to the discovery port.
4. Receive UDP `CONNECT_RESP`.
5. Open `ws://<host-ip>:<issued-port><issued-path>?token=<issued-token>`.
6. Wait for the first host text frame.
7. Send baseline `DeviceAppProfile`.
8. Start optional telemetry and screenshot streaming.

Timing to match:

- UDP connect timeout: `5s`
- WebSocket connect attempts: `12`
- per-attempt WebSocket timeout: `2s`
- retry delay: `250ms`
- initial host hello timeout: `10s`

Compatibility details:

- manual host IP is required today
- discovery port default is `45123`
- Studio currently issues an ephemeral WebSocket session port in `56500-56599`
- Studio path is `/ws`
- the initial host frame should be treated as opaque text even though Studio currently sends `HOST_HELLO`

### Ack model

The Android runtime must preserve the current ordering model:

- request/ack text sends are serialized
- only one request/ack send is in flight
- ack waits for the next inbound non-tool text message
- acknowledgements are not correlated by request id
- tool protocol responses must be intercepted so they never satisfy the ack wait

Current host ack behavior:

- ack `CLIENT_LOG`
- ack `CLIENT_DONE`
- ack `CLIENT_EVENTS`
- ack `DeviceAppProfile`
- do not ack `CLIENT_METRIC_CHANNELS`
- do not ack `CLIENT_METRICS`
- do not ack binary JPEG frames
- do not ack tool protocol responses

Timeouts to mirror:

- general send timeout: `10s`
- `DeviceAppProfile` ack timeout: `15s`
- `CLIENT_LOG` ack timeout: `15s`
- `CLIENT_EVENTS` ack timeout: `15s`
- `CLIENT_DONE` ack timeout: `10s`

### Telemetry messages

The runtime must emit:

- `CLIENT_LOG`
- `CLIENT_DONE`
- `CLIENT_METRIC_CHANNELS`
- `CLIENT_METRICS`
- `CLIENT_EVENTS`

Observed constraints:

- channel ids are bytes
- metric values are signed 64-bit integers semantically
- Studio parses metric values as `Int64`
- batch limit is `160`
- pending metric queue cap is `2000`

### DeviceAppProfile

The runtime must send one baseline `DeviceAppProfile` immediately after the WebSocket handshake.

Minimum payload:

- `type = "DeviceAppProfile"`
- `schema = "ansight.device-app-profile.v1"`
- `sentAt`
- `reasonCode`
- `profileSeq`
- `device`
- `app`
- `runtime`

Recommended Android runtime mapping:

- `runtime.primary = 3`
- `runtime.stack` includes Android plus the app framework layer when known
- for React Native or Flutter bridges, let the bridge provide overlay metadata that the Android runtime merges into the baseline profile

### Tool protocol

The Android runtime must implement:

- `tool.query`
- `tool.call`
- `tool.catalog`
- `tool.result`
- `tool.error`

Rules to match:

- intercept `tool.query` and `tool.call` before the normal ack queue
- ignore mismatched `capability`
- invalid tool request parse returns `tool.error` with `tool_protocol_invalid_request`
- response ids are `<requestId>.response`
- `replyTo` is the request id
- `payload.arguments` values are flattened to strings before execution

### Screenshot stream

The preferred screenshot transport is binary `ASJP` frames:

- 28 byte header
- JPEG bytes after the header

Header layout:

- bytes `0..3`: `ASJP`
- byte `4`: version `1`
- byte `5`: format `1`
- byte `6`: JPEG quality
- byte `7`: reserved `0`
- bytes `8..15`: Unix ms, little-endian `Int64`
- bytes `16..19`: width, little-endian `Int32`
- bytes `20..23`: height, little-endian `Int32`
- bytes `24..27`: JPEG byte count, little-endian `Int32`

Option validation to mirror:

- default interval: `2000ms`
- minimum interval: `250ms`
- default quality: `70`
- quality range: `1-100`
- default `maxWidth`: `720`
- clamp `maxWidth` to `8192`

## Android runtime architecture

### Public API

Recommended Kotlin-first API:

```kotlin
object Ansight {
    fun initialize(application: Application, options: AnsightOptions = AnsightOptions())
    fun activate()
    fun deactivate()
    fun clear()
    fun metric(value: Long, channel: Int = AnsightChannels.UNSPECIFIED)
    fun event(
        label: String,
        type: AnsightEventType = AnsightEventType.Info,
        details: String? = null,
        channel: Int = AnsightChannels.UNSPECIFIED,
        id: String? = null
    )

    suspend fun openSession(pairingJson: String, options: PairingOpenOptions): OpenSessionResult
    suspend fun completeSession()
    suspend fun closeSession()

    fun registerTool(tool: AnsightTool)
}
```

Design notes:

- use Kotlin types publicly
- accept channel ids as `Int`, validate `0-255`
- keep session APIs `suspend`
- expose a profile override path so bridges can add React Native or Flutter metadata

### Internal modules

Recommended split:

- `runtime-core`
- `pairing`
- `transport`
- `telemetry`
- `screenshot`
- `device-profile`
- `tools`

### Concurrency model

Recommended implementation:

- Kotlin coroutines
- one dedicated session actor/serial dispatcher for request/ack ordering
- independent pumps for:
  - metrics
  - events
  - screenshot capture

### Networking

Recommended implementation:

- UDP via standard Android/JVM networking
- WebSocket via a robust client implementation such as OkHttp WebSocket

Requirement:

- the send path must preserve the current one-in-flight request/ack model

### Device profile collection

Baseline Android profile should include:

- manufacturer, brand, model, product
- emulator detection
- OS version, build, API level
- ABI list
- display metrics
- battery state
- package name, version name, version code, debuggable

The runtime should let the bridge augment:

- runtime stack entries
- app tags
- framework-specific environment metadata

### Telemetry capture

Built-in Android telemetry should target:

- native heap
- RSS/resident memory where reliably available
- frame timing/FPS

Custom metrics and events must remain first-class and cheap to record.

### Screenshot capture

Preferred capture strategy:

- capture the current foreground `Activity` root surface from the native view hierarchy

Important limits:

- `SurfaceView`
- DRM video
- some `TextureView` or hardware-composited content
- platform views from bridged frameworks

may not render identically in all cases. The runtime should document screenshot semantics as "best-effort capture of the app's visible root surface."

### Tools

Built-in tools should live natively.

Likely first-class candidates:

- file system read/list tools
- database inspection tools
- screenshot tool

UI tree tools should be deferred until Android UI framework scope is clear:

- Views-only and Compose-only trees are different problems
- Flutter and React Native app trees should not be forced into one Android-native tool contract without framework-specific design

## Direct native app consumption

This runtime must be usable directly from:

- native Android apps
- apps built with Views
- apps built with Compose

Bridges are secondary consumers, not the primary integration path.

That means:

- public APIs must be stable without React Native or Flutter present
- no dependency on JS or Dart runtimes
- package initialization must work in plain native apps

## Bridge contract for React Native and Flutter

The Android runtime should expose narrow extension points for bridges:

- profile augmentation
- custom event and metric submission
- optional framework-specific tool registration
- optional framework metadata such as:
  - `runtime.primary`
  - `runtime.stack`
  - render backend hints
  - tags

The bridges must not own:

- pairing validation
- session transport
- screenshot framing
- tool guard logic

## Known gaps and limitations

### Android can do more than bridge layers

The native Android runtime can support privileged file/database/network/UI operations that React Native and Flutter bridges should not try to own.

### UI inspection is framework-specific

There is no honest single "visual tree" parity story yet across:

- native Android Views
- Compose
- React Native
- Flutter

Do not promise cross-framework UI tree parity in v1.

### Screenshot capture has edge cases

Some hardware surfaces and hybrid compositions may not capture exactly.

### Protocol gaps remain

The current protocol still lacks:

- automatic LAN discovery in the base client
- signed UDP request/response
- resumable sessions
- tool cancellation and progress
- host-driven screenshot control

## Delivery plan

### Phase 0

- define module structure
- define wire fixtures shared with iOS and bridge layers

### Phase 1

- local telemetry runtime
- options validation
- channels and retention

### Phase 2

- pairing document validation
- UDP/WebSocket transport
- baseline `DeviceAppProfile`

### Phase 3

- telemetry streaming parity

### Phase 4

- screenshot streaming parity

### Phase 5

- native built-in tools

### Phase 6

- packaging and bridge integration

## Testing plan

Required coverage:

- pairing fixtures and signature variants
- connect flow integration tests
- ack ordering tests
- telemetry batching tests
- `ASJP` encode/decode tests
- live Studio interop tests

Bridges should test against this runtime, not against their own protocol reimplementations.
