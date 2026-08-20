# Ansight Protocol

This document describes the current Ansight enrollment, connection, session,
telemetry, and remote-tool wire contracts. There is one supported enrollment
protocol. Older pairing handshakes and pairing-config formats are not accepted.

## Enrollment and connection

### Design goals

The default developer flow is deliberately small:

1. The app initializes and activates the SDK.
2. A host-local runtime registers automatically with a running, signed-in
   Studio through loopback.
3. Studio shows one generic, short-lived, one-use enrollment QR.
4. A physical app scans it and registers itself automatically.
5. Later launches reconnect automatically while the registration is valid.

There are no certificates, signing keys, pairing files, host-address fields, or
approval services for the developer to configure.

This protocol uses clear-text UDP and WebSocket traffic on the local network.
It is intended for trusted development networks. It does not provide
confidentiality or protection against an active network attacker.

### Data conventions

- JSON is UTF-8 and uses camel-case property names.
- Parsers may ignore unknown properties.
- Date-time fields use UTC ISO-8601 strings.
- WebSocket text messages contain one complete JSON document after reassembly.
- WebSocket binary messages are routed by their first four ASCII bytes.

### Enrollment invite

The QR contains either an enrollment invite document or its compact `ans2`
encoding.

Invite schema: `ansight.enrollment-invite.v2`

Document schema: `ansight.enrollment-invite-document.v2`

```json
{
  "schema": "ansight.enrollment-invite-document.v2",
  "invite": {
    "schema": "ansight.enrollment-invite.v2",
    "inviteId": "invite_123",
    "appId": "*",
    "appName": "Any Ansight app",
    "issuedAt": "2026-07-30T01:00:00Z",
    "expiresAt": "2026-07-30T01:10:00Z",
    "minProtocolVersion": 2,
    "allowedTransports": ["ws"],
    "host": {
      "hostId": "host_123",
      "hostName": "Developer Mac",
      "discoveryPort": 45123
    },
    "enrollment": {
      "accessToken": "unguessable-one-use-secret",
      "expiresAt": "2026-07-30T01:10:00Z",
      "grantExpiresAt": "2026-08-13T01:00:00Z",
      "maxUses": 1,
      "maxScopes": ["Read"],
      "allowCritical": false
    }
  },
  "discovery": {
    "schema": "ansight.discovery-hint.v1",
    "source": "studio-qr",
    "hostAddresses": ["192.0.2.10"],
    "discoveryPort": 45123,
    "hostName": "Developer Mac",
    "capturedAt": "2026-07-30T01:00:00Z"
  }
}
```

`discovery.hostAddresses` is ordered. Clients trim entries, remove empty
values, and de-duplicate them while preserving first-seen order.

The compact QR representation is:

```text
ans2:<base64url(gzip(utf8-json-enrollment-invite-document))>
```

The base64url value is unpadded.

`appId: "*"` is the v2 any-app target. Studio uses it for the generic QR.
The invite does not pre-register an app and the SDK never sends `*` as its
runtime identity. The scanning SDK supplies its real package or bundle id in
`ENROLLMENT_CONNECT`; Studio creates the app record after successful
authorization. Existing app-specific v2 invites retain their exact app-id
check.

### Installation identity

Each SDK generates a random, stable `deviceId` and local enrollment token in
app-private storage. It does not use a hardware identifier and does not require
a platform permission.

For host-local runtimes, Studio creates or reuses a local grant keyed by the
app id, installation id, and token. Local enrollment is accepted only when the
UDP request originated from loopback and Studio has an authenticated account.
The SDK checks its well-known installed and source-build Studio ports with
short loopback-only timeouts before trying older stored registrations. It
retries while active, so Studio does not need to be running during the app
build or initial launch.

The first successful use of an invite atomically binds its access token to:

- `inviteId`
- `appId`
- `deviceId`

For a generic invite, `appId` is the scanning app's runtime package or bundle
id, not the `*` target carried by the QR. The same app installation can
reconnect with that saved invite and device id.
A different installation cannot claim the consumed invite. Clearing app data
or removing the saved registration creates a new installation identity and
requires a fresh QR.

The QR expiry limits new registration. An already registered installation may
continue reconnecting until `grantExpiresAt`.

### Host address resolution

The client tries, in order:

1. an explicit caller override, when supplied;
2. simulator/emulator loopback, when available;
3. addresses in the QR discovery hint;
4. addresses remembered after successful sessions.

The SDK does not enumerate LAN devices or Bonjour services.

### UDP enrollment request

The client sends `ENROLLMENT_CONNECT` to the selected host address and
discovery port.

```json
{
  "type": "ENROLLMENT_CONNECT",
  "ver": 2,
  "requestId": "request_123",
  "enrollmentMode": "invite",
  "inviteId": "invite_123",
  "appId": "com.example.app",
  "deviceId": "installation_123",
  "deviceName": "Example on iPhone",
  "accessToken": "saved-enrollment-secret",
  "processSessionId": "process_123"
}
```

Required properties are `type`, `ver`, `requestId`, `enrollmentMode`,
`inviteId`, `appId`, `deviceId`, and `accessToken`. `processSessionId` remains
stable for the life of the app process.

`enrollmentMode` is `invite` for QR and saved physical-device registrations.
It is `local` for automatic host-local registration. A local request uses an
SDK-generated `local:<appId>` identifier and stable app-private token; it does
not contain or depend on an invite issued at build time.

### UDP enrollment result

The host returns `ENROLLMENT_RESULT` from the selected host address. The client
accepts only a result whose `requestId` matches its request.

```json
{
  "type": "ENROLLMENT_RESULT",
  "ver": 2,
  "requestId": "request_123",
  "accepted": true,
  "reason": "accepted",
  "reasonMessage": "Installation registered.",
  "hostId": "host_123",
  "hostName": "Developer Mac",
  "hostWifiName": "Development",
  "message": "Connected.",
  "webSocketPort": 45124,
  "webSocketPath": "/ws",
  "webSocketToken": "short-lived-session-token"
}
```

An accepted result must include `webSocketPort`, `webSocketPath`, and a
short-lived `webSocketToken`. Rejections use a machine-readable `reason` and a
human-readable `reasonMessage`.

The WebSocket URL is:

```text
ws://<host-address>:<webSocketPort><webSocketPath>?token=<webSocketToken>
```

No TLS certificate or certificate trust setup is involved.

## Live session

After the WebSocket opens, the client:

1. sends `session.open` and waits for its correlated response;
2. sends `device.profile` when available;
3. starts lifecycle, telemetry, touch, screenshot, property, and log streams;
4. answers permitted remote-tool requests;
5. sends `session.complete` when intentionally completing the session.

### Control messages

Control requests use:

```json
{
  "type": "CONTROL_REQ",
  "requestId": "request_456",
  "action": "session.open",
  "payload": {}
}
```

Responses use:

```json
{
  "type": "CONTROL_RESP",
  "requestId": "request_456",
  "success": true,
  "message": "Session opened.",
  "payload": {}
}
```

Supported actions include `session.open`, `session.properties`,
`device.profile`, `app.state`, `client.log`, and `session.complete`.

### Crash handoff

Crash reporting is a two-process protocol. A fatal handler never opens a
network connection. It writes a bounded record into app-private storage and
allows the operating system's normal termination path to continue. On the next
healthy launch, the SDK combines that record with the previous
`processSessionId`, open Studio/offline session identifiers, recent
breadcrumbs, and OS termination diagnostics.

When Studio is connected, the recovering process sends an acknowledged
`crash.handoff` control request:

```json
{
  "type": "CONTROL_REQ",
  "requestId": "request_crash_1",
  "action": "crash.handoff",
  "payload": {
    "reportId": "6f914d7bdca928cfbf6e9691aefa0a42",
    "targetProcessSessionId": "process_that_crashed",
    "targetSessionId": "previous_studio_session_if_known",
    "deliveryProcessSessionId": "recovering_process",
    "report": {
      "schema": "ansight.crash.v1",
      "platform": "android",
      "kind": "native_crash",
      "confidence": "confirmed",
      "occurredAtUtc": "2026-08-13T00:00:00.000Z",
      "candidate": {},
      "termination": {},
      "breadcrumbs": [],
      "traceBase64": "optional-bounded-os-trace"
    }
  }
}
```

Studio uses `reportId` as an idempotency key and associates the report with
`targetSessionId` or `targetProcessSessionId`. It returns a successful
`CONTROL_RESP` only after the report is durably stored. The SDK then marks the
Studio delivery complete. Failed and interrupted requests remain in the
bounded outbox for a later connection.

If an offline capture was open when the process died, `.NET` recovery writes
the same report under `diagnostics/crashes` in that prior capture, seals its
manifest with the crash termination kind, and acknowledges the offline copy.
The normal offline ZIP/upload path consequently transports the crash without
network work in the fatal handler. A report is removed only after every
delivery route required by its prior-session associations has succeeded.

### Telemetry streams

Telemetry is sent as WebSocket text messages:

- `CLIENT_METRIC_CHANNELS` describes channel metadata.
- `CLIENT_METRICS` carries timestamped numeric samples.
- `CLIENT_EVENTS` carries timestamped events.
- `CLIENT_TOUCH_INPUT` carries `ansight.touches.v1` packed touch batches.
- `CLIENT_VISUAL_TREE` carries screenshot-aligned or touch-triggered visual-tree snapshots.

Touch-triggered visual trees use `source: "sdk.touchCapture"`. They omit
`screenshotCapturedAtUtc` and carry the gesture correlation both on the event
and inside the tree payload so recorders that preserve only one layer retain
the trigger:

```json
{
  "type": "CLIENT_VISUAL_TREE",
  "capturedAtUtc": "2026-08-13T00:00:00.250Z",
  "source": "sdk.touchCapture",
  "captureTrigger": "touch",
  "gestureId": "gesture-37f5...",
  "gesturePhase": "checkpoint",
  "touchAction": "move",
  "touchCapturedAtUtc": "2026-08-13T00:00:00.120Z",
  "payload": {
    "captureTrigger": {
      "kind": "touch",
      "gestureId": "gesture-37f5...",
      "gesturePhase": "checkpoint",
      "touchAction": "move",
      "touchCapturedAtUtc": "2026-08-13T00:00:00.120Z"
    }
  }
}
```

The phases are `started`, `checkpoint`, `ended`, and `cancelled`. Current SDKs
capture at the leading down, every 250 ms while any pointer in the gesture is
active, and at the terminal up or cancel. Checkpoints are coalesced when a tree
capture is slower than the interval.

These streams are fire-and-forget. Session and tool operations use correlated
request/response envelopes.

### Binary streams

WebSocket binary messages use four-byte magic values:

- `ASFT` for `ansight.file-transfer.v1` tool and artifact transfers.
- `ASJP` for live JPEG screenshot frames.

`ASJP` version 1 uses a 28-byte header. Byte 7 is a flags field. Bit 0
(`0x01`) means keyboard presence is known for the frame, and bit 1 (`0x02`)
means the on-screen keyboard was present. A zero flags byte, including frames
from older SDKs and frames captured without explicit keyboard-presence opt-in,
means keyboard presence is unknown rather than absent. Hosts must only read bit
1 when bit 0 is set and must ignore unrecognized flag bits.

## Remote Tool Protocol

The host can query registered tools and invoke tools permitted by the SDK's
tool guard.

### Query

```json
{
  "type": "tool.query",
  "requestId": "tool_query_1"
}
```

The client responds with its catalog of tool ids, descriptions, argument
schemas, scopes, and security metadata.

### Call

```json
{
  "type": "tool.call",
  "requestId": "tool_call_1",
  "toolId": "ui.get_visual_tree",
  "arguments": {}
}
```

The response carries the same `requestId`, a success flag, and either a result
or a structured error. Large binary results may be transferred through the
`ASFT` stream and referenced by transfer id.

### Artifact Tools

`artifacts.query` returns the app-provided artifact catalog.
`artifacts.request` creates one catalog artifact and returns inline text,
inline bytes, or an `ASFT` transfer reference. Both are read-scoped and remain
subject to the local tool guard.

### Guard

The client enforces the configured tool guard before invoking a tool:

- disabled
- read-only
- read-write
- full access

The enrollment invite's scope ceiling can further restrict access. The host
cannot raise permissions beyond the app's local configuration.

## Operational behavior

- Enrollment invites are bearer secrets. Do not publish, log, or ship them in
  production resources.
- Local enrollment is rejected unless the datagram source is loopback and
  Studio is authenticated.
- The host consumes first registration atomically.
- A registered installation reconnects with its saved state and does not need
  the original QR to remain unexpired.
- The WebSocket token is ephemeral and issued per accepted UDP request.
- Cellular connections are disabled by default by SDK policy.
- Clear-text transport is a deliberate low-friction development trade-off;
  use only on a network you trust.
