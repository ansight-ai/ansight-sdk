# Ansight Protocol

This file contains the current Ansight connection, transport, and remote-tool protocol.

## Connection Protocol

This document describes the protocol behavior currently implemented by the
reference Ansight client, expressed as a data and transport contract. It is for
anyone building a compatible client runtime, host, bridge, or protocol parser in
any implementation language.

This is descriptive, not a wishlist. If a message, negotiation step, or recovery
behavior is not listed here, the current reference client does not depend on it.
Product APIs, UI flows, local storage choices, runtime collection internals, and
language-specific client-library structure are outside this document.

### Protocol Overview

The current protocol behavior is made of these parts:

- pairing material: signed JSON that identifies the target app, host key,
  expiry, one-time token, and optional discovery hints
- UDP bootstrap: `CONNECT_REQ` and `CONNECT_RESP` datagrams used to exchange a
  live WebSocket handoff
- WebSocket session: the authenticated live transport opened with the
  host-issued handoff token
- control plane: correlated `CONTROL_REQ` and `CONTROL_RESP` text envelopes for
  session lifecycle and state messages
- telemetry streams: fire-and-forget JSON text messages for metric and event
  batches
- input stream: compact JSON text batches for captured touch input
- tool plane: request/response JSON envelopes for remote tool discovery and
  execution
- binary streams: WebSocket binary messages for tool-produced assets (`ASFT`)
  and live screenshot frames (`ASJP`)

In one sentence: the client validates a signed pairing document, asks the host
over UDP for a temporary WebSocket endpoint, opens that WebSocket, then sends
small JSON control messages, JSON stream batches, and a few binary frame types
over the same socket.

### Actors

- host: the machine or service that issues pairing material, accepts UDP
  bootstrap packets, and owns the live WebSocket endpoint
- client runtime: the runtime embedded in the target app or process that
  validates pairing material and initiates the connection
- bridge: optional software that consumes host-side protocol data and exposes it
  to another tool system

The client runtime initiates both the UDP bootstrap and the WebSocket session.
After the WebSocket opens, the current client sends control requests and stream
messages, answers host-initiated tool requests, and may send binary stream
frames.

### Data Conventions

- JSON messages are UTF-8 encoded and use camel-case property names.
- `type` is the primary discriminator for top-level message families.
- `schema` identifies versioned payload schemas when present.
- Current JSON parsers ignore unknown object properties.
- Optional fields may be omitted or set to `null` unless this document marks
  them as required.
- Date-time JSON fields use UTC ISO-8601 strings unless a field explicitly says
  it is a Unix millisecond value. Current examples use `+00:00` for
  `DateTimeOffset` values and `Z` for UTC `DateTime` values.
- Binary integer fields are little-endian.
- WebSocket text messages contain one complete JSON document after WebSocket
  message reassembly.
- WebSocket binary messages are routed by their first four ASCII bytes.

### Default Transport Values

These constants identify the v1 default transport values:

- UDP discovery port: `45123`
- WebSocket port: `45124`
- WebSocket path: `/ws`

The discovery port is used when no explicit port, discovery hint, or config
value supplies one. The WebSocket port and path are host-side defaults; an
accepted UDP `CONNECT_RESP` must still return the WebSocket port, path, and
token to use for the live session.

### Current Client Flow

The current high-level client flow is:

1. Accept a pairing input as a JSON pairing config, JSON pairing config
   document, or compact pairing config code.
2. Parse the input and normalize any discovery host-address list.
3. Validate the pairing config signature, expiry, and optional expected app id.
4. Resolve a host address and UDP discovery port.
5. Send a UDP `CONNECT_REQ` to the selected host address and discovery port.
6. Accept only a `CONNECT_RESP` received from the selected host address.
7. If the response is accepted, require `webSocketPort`, `webSocketPath`, and
   `webSocketToken`.
8. Open `ws://<host-address>:<webSocketPort><webSocketPath>?token=<token>`.
9. Attach the WebSocket transport and start a receive loop.
10. Send `session.open` and wait for a correlated `CONTROL_RESP`.
11. Send `device.profile` when a baseline profile is available and wait for a
    correlated `CONTROL_RESP`.
12. Start app-state streaming and send the current `app.state`.
13. Start runtime custom-property update streaming when the runtime is
    initialized.
14. Start screenshot streaming when the runtime is initialized and screenshot
    capture is configured.
15. Save a remembered connection profile after a successful session.
16. In the runtime-owned host connection manager, start metrics streaming and
    touch streaming after the session opens.
17. Send `session.complete` when intentionally completing the session, then
    close the WebSocket.

### Host Behavior the Client Expects

This is not a full host design. It is only the behavior the current client
needs from a host to continue through the flow above:

1. The host provides pairing material containing a signed pairing config.
2. The host listens for UDP `CONNECT_REQ` packets on the advertised discovery
   port.
3. The host returns `CONNECT_RESP` with `accepted = false` and a reason when it
   rejects the request.
4. When accepted, the host returns `webSocketPort`, `webSocketPath`, and
   `webSocketToken`.
5. The host accepts the WebSocket connection only when the `token` query string
   is valid.
6. The host replies to every supported `CONTROL_REQ` with a correlated
   `CONTROL_RESP`.
7. The host parses client stream messages by top-level `type`.
8. The host routes binary WebSocket messages by magic header: `ASFT` or `ASJP`.
9. The host may send `tool.query` and `tool.call` messages; the client handles
   those automatically when the runtime and tool guard allow it.

### Pairing Material

Pairing material is the trust and discovery data needed before any transport is
opened.

#### Pairing Config

Schema: `ansight.pairing-config.v1`

Required fields:

- `schema`: schema id
- `configId`: stable id for this pairing config
- `appId`: target app or process id
- `appName`: human-readable target name
- `issuedAt`: UTC issue time
- `expiresAt`: UTC expiry time
- `oneTimeToken`: token presented in the UDP connect request
- `host`: host identity and signing key metadata
- `challenge`: host challenge metadata
- `signature`: base64 signature over the canonical config payload

Example:

```json
{
  "schema": "ansight.pairing-config.v1",
  "configId": "cfg_123",
  "appId": "target.app",
  "appName": "Target App",
  "issuedAt": "2026-03-20T09:55:00+00:00",
  "expiresAt": "2026-03-20T10:10:00+00:00",
  "oneTimeToken": "token_123",
  "host": {
    "hostPubKey": "base64-subject-public-key-info",
    "hostPubKeyFingerprint": "sha256:..."
  },
  "challenge": {
    "alg": "ECDH-P256",
    "challengePubKey": "base64-or-protocol-key-material",
    "requireProofOnFirstPair": true
  },
  "signature": "base64-signature"
}
```

`host.hostPubKey` and `host.hostPubKeyFingerprint` are required for validation.
The current model can also carry host identity or transport fields such as
`hostId`, `hostName`, or `discoveryPort`, but those fields are not part of the
canonical signature payload.

#### Pairing Config Document

Schema: `ansight.pairing-config-document.v1`

The document wraps a signed config with optional discovery metadata. Skeleton
with the nested config shortened:

```json
{
  "schema": "ansight.pairing-config-document.v1",
  "config": {
    "schema": "ansight.pairing-config.v1"
  },
  "discovery": {
    "schema": "ansight.discovery-hint.v1",
    "source": "qr",
    "hostAddresses": ["192.0.2.10"],
    "discoveryPort": 45123,
    "hostName": "host-1",
    "wifiName": "network-name",
    "capturedAt": "2026-03-20T09:55:00+00:00"
  }
}
```

`discovery.hostAddresses` is an ordered list of candidate IP address strings.
The current client trims entries, removes empty entries, and de-duplicates
addresses case-insensitively while preserving first-seen order.

#### Compact Pairing Config Code

Compact pairing codes are QR-friendly encodings of a pairing config document:

```text
apc1:<base64url(gzip(utf8-json-pairing-config-document))>
```

The base64url value is unpadded.

#### Signature Validation

To produce a config accepted by the current client, sign the UTF-8 bytes of the
compact JSON object below, without the `signature` field and with properties in
the order shown:

```json
{
  "schema": "ansight.pairing-config.v1",
  "configId": "cfg_123",
  "appId": "target.app",
  "appName": "Target App",
  "issuedAt": "2026-03-20T09:55:00+00:00",
  "expiresAt": "2026-03-20T10:10:00+00:00",
  "oneTimeToken": "token_123",
  "host": {
    "hostPubKey": "base64-subject-public-key-info",
    "hostPubKeyFingerprint": "sha256:..."
  },
  "challenge": {
    "alg": "ECDH-P256",
    "challengePubKey": "base64-or-protocol-key-material",
    "requireProofOnFirstPair": true
  }
}
```

The current trust model uses ECDSA with SHA-256. Current pairing configs use
P-256 key material, but the validator imports the ECDSA public key from
`host.hostPubKey` and does not separately enforce the challenge algorithm.
`host.hostPubKey` is base64 encoded SubjectPublicKeyInfo. `signature` is base64
encoded IEEE P1363 fixed-field signature bytes (`r || s`, 64 bytes for P-256),
not a DER sequence. UTC `issuedAt` and `expiresAt` values in canonical JSON use
an explicit offset such as `+00:00`.

The current validator:

- parse the public key from `host.hostPubKey`
- verify `signature` over the canonical JSON bytes
- reject configs whose `expiresAt` is in the past
- reject configs whose `appId` does not match the expected app id when an
  expected app id is supplied

### Host Address and Port Resolution

The current client does not perform automatic LAN or multicast discovery. It
needs at least one host-address candidate before it can connect.

Current host-address candidate order:

1. Explicit host-address override supplied by the caller, when present.
2. Simulator or emulator local-host fallback, when detected.
3. `discovery.hostAddresses` from the pairing config document, after
   normalization.

When an explicit host-address override is present, the current client uses only
that address. It does not append simulator fallback or discovery addresses.

Current UDP discovery port order:

1. Explicit discovery-port override supplied by the caller, when present.
2. `discovery.discoveryPort`.
3. `config.host.discoveryPort`, when present and in the range `1..65535`.
4. Default UDP discovery port `45123`.

There is also a local Wi-Fi preflight check. When the runtime can tell that the
device is not on Wi-Fi, and the candidate list does not include a simulator
local-host address, the current client fails before sending UDP.

### UDP Bootstrap

The client sends one UTF-8 JSON datagram to the selected host address and UDP
discovery port.

For each host-address candidate, the current client allows about five seconds
for the UDP bootstrap before moving to the next candidate or failing.

#### `CONNECT_REQ`

Required fields:

- `type`: `CONNECT_REQ`
- `ver`: `1`
- `configId`: pairing config id
- `oneTimeToken`: pairing config one-time token
- `appId`: pairing config app id
- `clientName`: human-readable client name

Optional fields:

- `processSessionId`: stable id for this client runtime process lifetime

Example:

```json
{
  "type": "CONNECT_REQ",
  "ver": 1,
  "configId": "cfg_123",
  "oneTimeToken": "token_123",
  "appId": "target.app",
  "clientName": "Target App",
  "processSessionId": "proc_123"
}
```

#### `CONNECT_RESP`

The current client accepts only responses that:

- are received from the selected host IP address
- parse as JSON
- have `type` exactly equal to `CONNECT_RESP`

Required fields:

- `type`: `CONNECT_RESP`
- `ver`: `1`
- `accepted`: boolean host decision
- `reason`: machine-readable reason code
- `hostId`: stable host id
- `hostName`: human-readable host name
- `message`: human-readable status message

Optional fields:

- `reasonMessage`: detailed rejection or status message
- `hostWifiName`: Wi-Fi network name reported by the host
- `webSocketPort`: required when `accepted` is `true`
- `webSocketPath`: required when `accepted` is `true`
- `webSocketToken`: required when `accepted` is `true`

Example:

```json
{
  "type": "CONNECT_RESP",
  "ver": 1,
  "accepted": true,
  "reason": "ok",
  "reasonMessage": null,
  "hostId": "host_123",
  "hostName": "host-1",
  "hostWifiName": "network-name",
  "message": "ready",
  "webSocketPort": 45124,
  "webSocketPath": "/ws",
  "webSocketToken": "ws_token"
}
```

If `accepted` is `false`, the current client does not open a WebSocket and
surfaces the host rejection. If `accepted` is `true` but any WebSocket handoff
field is missing or empty, the connection attempt fails.

The UDP request and response are not separately signed in protocol v1. Trust is
based on signed pairing material, the one-time token, host source-address
filtering, and the WebSocket handoff token.

### WebSocket Session

After an accepted UDP response, the client opens:

```text
ws://<host-address>:<webSocketPort><webSocketPath>?token=<url-encoded-webSocketToken>
```

The current client tries to open the WebSocket up to 12 times. Each attempt has
a two-second connect timeout, and retries wait 250 ms.

The current client serializes WebSocket writes so text and binary messages do
not interleave at the application layer. Individual sends use a ten-second send
timeout.

### WebSocket Message Routing

Text messages are routed by JSON `type`.

Client-side receive routing:

- `tool.query` and `tool.call` with `capability = "tool.exec"` are handled by
  the tool plane and answered on the same WebSocket.
- `CONTROL_RESP` is matched to a pending control request by `replyTo`.
- Other text messages are logged as unexpected and ignored.

Host-side receive routing:

- `CONTROL_REQ` is handled by the control plane and answered with
  `CONTROL_RESP`.
- `CLIENT_METRIC_CHANNELS`, `CLIENT_METRICS`, `CLIENT_EVENTS`, and
  `CLIENT_TOUCH_INPUT` are stream payloads.
- `tool.catalog`, `tool.result`, and `tool.error` answer host-initiated tool
  requests.

Binary messages are routed by their magic header:

- `ASFT`: tool-produced binary transfer frame
- `ASJP`: live screenshot JPEG frame

### Control Plane

The control plane uses correlated request/response text envelopes.

The current client generates request ids in the form
`client.<32-lowercase-hex-guid>`, sends one control request at a time, and waits
for a `CONTROL_RESP` whose `replyTo` equals that request id.

#### Request Envelope

```json
{
  "type": "CONTROL_REQ",
  "id": "client.0123456789abcdef",
  "replyTo": null,
  "action": "session.open",
  "payload": {},
  "success": true,
  "message": null
}
```

Fields:

- `type`: `CONTROL_REQ`
- `id`: required request id
- `replyTo`: normally `null` on requests
- `action`: action name
- `payload`: action-specific JSON object or `null`
- `success`: ignored on requests
- `message`: ignored on requests

#### Response Envelope

```json
{
  "type": "CONTROL_RESP",
  "id": "host.response.1",
  "replyTo": "client.0123456789abcdef",
  "action": "session.open",
  "payload": null,
  "success": true,
  "message": "ok"
}
```

Fields:

- `type`: `CONTROL_RESP`
- `id`: response id
- `replyTo`: id of the request being answered
- `action`: action being answered
- `payload`: optional action-specific response data
- `success`: whether the request succeeded
- `message`: optional status or error text

The current client fails the pending operation when the response has
`success = false` or when no correlated response arrives before the action
timeout. A timeout also closes the WebSocket transport. Timeouts are 15 seconds
for `session.open`, `device.profile`, `app.state`, and `client.log`; 10 seconds
for `session.properties` and `session.complete`.

### Control Actions

#### `session.open`

Sent by the client after the WebSocket is attached.

Payload:

```json
{
  "clientName": "Target App",
  "configId": "cfg_123",
  "appId": "target.app",
  "openedAtUtc": "2026-03-20T10:00:00+00:00",
  "customProperties": {
    "group": {
      "key": "value"
    }
  }
}
```

`customProperties` is optional. It is a grouped JSON object:
`customProperties[group][key] = scalar-json-value`. Group and key names are
trimmed when registered by the current client, and custom property values cannot
be objects or arrays.

#### `session.properties`

Sent by the client when the session property bag changes.

Payload:

```json
{
  "customProperties": {
    "group": {
      "key": "value"
    }
  },
  "updatedAtUtc": "2026-03-20T10:02:00+00:00"
}
```

The payload carries the current full grouped property bag. An empty
`customProperties` object clears previously registered custom properties for the
session.

#### `device.profile`

Sent by the client after `session.open` when a baseline profile is available.

Payload schema: `ansight.device-app-profile.v1`

Top-level payload fields:

- `type`: `DeviceAppProfile`
- `schema`: `ansight.device-app-profile.v1`
- `sentAt`: Unix milliseconds
- `reasonCode`: numeric reason code
- `profileSeq`: sequence number within the session
- `sdk`: metadata for the client implementation
- `device`: device or host environment metadata
- `app`: target app or process metadata
- `runtime`: runtime stack metadata
- `graphics`: render configuration metadata
- `permissions`: string map keyed by permission name
- `tags`: string labels

Example:

```json
{
  "type": "DeviceAppProfile",
  "schema": "ansight.device-app-profile.v1",
  "sentAt": 1773972000000,
  "reasonCode": 1,
  "profileSeq": 1,
  "sdk": {
    "name": "ansight-runtime",
    "packageId": "runtime.package",
    "version": "1.0.0",
    "language": "implementation-language"
  },
  "device": {
    "manufacturer": "vendor",
    "model": "model",
    "formFactor": "phone",
    "isVirtual": false,
    "isEmulator": false,
    "osName": "os",
    "osVersion": "1.0",
    "cpuArch": "arm64"
  },
  "app": {
    "appId": "target.app",
    "appName": "Target App",
    "versionName": "1.0.0",
    "buildNumber": "100"
  },
  "runtime": {
    "primary": 1,
    "primaryVersion": "1.0",
    "engine": {
      "name": "runtime-engine",
      "version": "1.0"
    },
    "stack": [
      {
        "runtimeCode": 1,
        "name": "runtime",
        "version": "1.0"
      }
    ]
  },
  "graphics": {
    "renderBackendCode": 1,
    "fpsTarget": 60,
    "vsyncEnabled": true
  },
  "permissions": {
    "camera": "granted"
  },
  "tags": ["debug"]
}
```

Known normalized `device.formFactor` values are `phone`, `tablet`, `desktop`,
`tv`, `watch`, `car`, `vr`, and `unknown`.

The current client sends the fields it can determine and omits fields it cannot.
Consumers need to tolerate missing nested objects.

#### `app.state`

Sent by the client after the WebSocket opens and whenever the foreground state
changes.

Payload:

```json
{
  "state": "foreground",
  "changedAtUtc": "2026-03-20T10:00:00+00:00"
}
```

Known `state` values:

- `foreground`
- `background`
- `unknown`

#### `client.log`

Sent by the client when a log line is explicitly forwarded to the host.

Payload:

```json
{
  "data": "log line"
}
```

#### `session.complete`

Sent by the client before intentionally closing the live session.

Payload:

```json
{
  "reason": "client log stream complete"
}
```

After the correlated response is received or the operation fails, the client
closes the WebSocket.

### Telemetry Text Streams

Telemetry stream messages are WebSocket text messages. They do not use
`CONTROL_REQ` and no acknowledgement is expected.

When metrics streaming starts, the current host connection manager starts it
after `session.open`, `device.profile`, and `app.state` have completed. The
current streamer announces metric channels first, seeds the stream with the last
160 existing metrics, and then sends new metric and event batches while the
session remains open.

#### `CLIENT_METRIC_CHANNELS`

Announces metric channel metadata. A channel id only needs to be announced once
per session. The current client sends only channels it has not already announced
on that session.

```json
{
  "source": "client",
  "type": "CLIENT_METRIC_CHANNELS",
  "sentAtUtc": "2026-03-20T10:00:00+00:00",
  "channels": [
    {
      "id": 42,
      "name": "render",
      "color": "#FF8800"
    }
  ]
}
```

#### `CLIENT_METRICS`

Sends metric samples.

```json
{
  "source": "client",
  "type": "CLIENT_METRICS",
  "sentAtUtc": "2026-03-20T10:00:00+00:00",
  "metrics": [
    {
      "channel": 42,
      "value": 123,
      "capturedAtUtc": "2026-03-20T09:59:58Z"
    }
  ]
}
```

#### `CLIENT_EVENTS`

Sends app or runtime events.

```json
{
  "source": "client",
  "type": "CLIENT_EVENTS",
  "sentAtUtc": "2026-03-20T10:00:00+00:00",
  "events": [
    {
      "id": "3c94dd4b-5dcb-4d11-a276-b0f2630f5e4e",
      "label": "Navigation changed",
      "eventType": "Navigation",
      "details": "detail",
      "capturedAtUtc": "2026-03-20T09:59:59Z",
      "channel": 42
    }
  ]
}
```

Current telemetry batching behavior:

- metric batches contain at most 160 samples
- pending metric queue is capped at 2000 samples; oldest pending samples are
  dropped first
- event batches contain at most 160 events
- pending events are de-duplicated by `id`
- pending events do not have a separate hard cap in the current streamer
- the metric and event pumps wake on new data or after roughly 500 ms
- consumers process received items in array order

### Touch Input Text Stream

Touch capture uses a compact WebSocket text message separate from telemetry
metrics and events. No acknowledgement is expected.

The current host connection manager starts touch streaming after metrics
streaming. If touch capture is not enabled, startup succeeds and no touch
messages are sent.

Schema: `ansight.touches.v1`

```json
{
  "type": "CLIENT_TOUCH_INPUT",
  "schema": "ansight.touches.v1",
  "t0": "2026-03-20T09:59:59+00:00",
  "space": "w",
  "unit": "px",
  "surface": [480, 800, 2],
  "rows": [
    [0, 0, 7, 120, 240],
    [16, 1, 7, 130, 250],
    [32, 2, 7, 130, 250, 0, 1]
  ]
}
```

Fields:

- `t0`: UTC timestamp used as the base time for all rows in this message
- `space`: coordinate space code; `w` means window
- `unit`: coordinate unit code; known values are `px`, `pt`, and `n`
- `surface`: `[width, height, scale]`
- `rows`: compact touch rows

Row columns:

- index `0`: `deltaMs`, milliseconds after `t0`
- index `1`: action code
- index `2`: pointer id
- index `3`: x coordinate
- index `4`: y coordinate
- index `5`: pointer index, optional
- index `6`: pointer count, optional

Action codes:

- `0`: down
- `1`: move
- `2`: up
- `3`: cancel
- `4`: unknown

Rows in one message share the same `space`, `unit`, and `surface` metadata. The
current client splits batches when those values differ.

Current touch batching behavior:

- each pump pass sends at most 200 pending touch records
- pending touch queue is capped at 2000 records; oldest pending records are
  dropped first
- the pump wakes on new touch input or after roughly 250 ms
- Android-style window pixels encode as `space = "w"` and `unit = "px"`
- Apple-style window points encode as `space = "w"` and `unit = "pt"`

### Tool Protocol

The tool protocol runs on the same WebSocket text channel. It is documented in
[Remote Tool Protocol](#remote-tool-protocol).

Overview:

- host sends `tool.query` to request the visible tool catalog
- client replies with `tool.catalog`
- host sends `tool.call` to execute a registered tool
- client replies with `tool.result` or `tool.error`
- all tool envelopes use `id` and `replyTo` for correlation
- current capability id is `tool.exec`

Envelope shape:

```json
{
  "type": "tool.call",
  "id": "req_1",
  "replyTo": null,
  "sessionId": "sess_1",
  "sentAt": "2026-03-20T10:00:00+00:00",
  "capability": "tool.exec",
  "payload": {}
}
```

Tool results are ordinary JSON payloads unless a specific tool announces an
out-of-band binary stream such as `ansight.file-transfer.v1`.

### Binary Tool Transfer Stream

Binary tool transfer uses WebSocket binary messages. It is used when a tool
needs to return bytes outside the JSON `tool.result` envelope, such as a file
download, an application-state artifact, or a one-shot screenshot capture. A
JSON tool result announces the transfer metadata first, including:

- `downloadId` or equivalent request id
- `transferId`
- asset metadata such as name, extension, MIME type, size, and version
- `deliveryMode = "websocket_binary"`
- `wireProtocol = "ansight.file-transfer.v1"`

After the announcing `tool.result` is sent, the client emits `ASFT` frames on
the same WebSocket.

Each WebSocket binary message contains one `ASFT` frame:

1. 56-byte header
2. zero or more payload bytes

Header layout:

| Bytes | Type | Description |
| --- | --- | --- |
| `0..3` | ASCII | magic `ASFT` |
| `4` | UInt8 | protocol version, currently `1` |
| `5` | UInt8 | frame type |
| `6..7` | UInt8 | reserved, currently `0` |
| `8..39` | ASCII | `transferId` as 32 lowercase hex characters |
| `40..43` | Int32 LE | sequence number |
| `44..51` | Int64 LE | byte offset for this frame |
| `52..55` | Int32 LE | payload byte count |

Frame types:

- `1`: chunk; payload contains transfer bytes
- `2`: complete; payload is empty
- `3`: error; payload contains a UTF-8 error message

The first chunk uses sequence `0` and offset `0`. The complete frame uses the
next sequence number and the final byte offset.

The host or bridge owns host-local temp paths. The client only sends transfer
metadata and bytes. Live periodic screenshot streaming does not use `ASFT`; it
uses the separate `ASJP` stream below.

### Screenshot Binary Stream

Screenshot streaming is optional and client-driven. There is no v1 host
negotiation message for enabling or controlling it.

Each WebSocket binary message contains one `ASJP` screenshot payload after
WebSocket message reassembly:

1. 28-byte header
2. JPEG bytes

Header layout:

| Bytes | Type | Description |
| --- | --- | --- |
| `0..3` | ASCII | magic `ASJP` |
| `4` | UInt8 | protocol version, currently `1` |
| `5` | UInt8 | image format, currently `1` for JPEG |
| `6` | UInt8 | JPEG quality |
| `7` | UInt8 | reserved, currently `0` |
| `8..15` | Int64 LE | capture time as Unix milliseconds |
| `16..19` | Int32 LE | width in pixels |
| `20..23` | Int32 LE | height in pixels |
| `24..27` | Int32 LE | JPEG byte count |

The current screenshot streamer starts only when the runtime is initialized and
session JPEG capture options are configured. Defaults are a 2000 ms interval,
quality 60, and max width 720 pixels. Capture is client-driven and
backpressure-driven: the next interval does not start until the previous capture
has finished encoding and sending. Captures are skipped while the current app
lifecycle state is `background`.

Consumers route frames by `ASJP`, validate the byte count, then decode the JPEG
payload.

### Remembered Connection Profiles

Remembered host profiles are current local client behavior, not a wire message.
After a successful session, the current client stores a validated pairing
document plus the last connected host address, host name, Wi-Fi name, discovery
port, and capture time.

Current cache behavior:

- cache file schema is `ansight.cached-pairing-profiles.v1`
- default retention is 14 days
- profiles are keyed by Wi-Fi name, or `wifi:<unknown>` when unavailable
- loading removes expired profiles and profiles that no longer validate
- reconnect tries valid profiles newest-first
- successful reconnect refreshes the stored host metadata and expiry
- profiles are cleared after pairing, token, proof, UDP bootstrap, or stale
  cached-address failures

### Out of Scope

The current implementation does not define:

- automatic LAN or multicast discovery
- signed UDP connect requests or responses
- a signed discovery response format
- a structured `host_hello` or `session_ready` envelope
- negotiated capability exchange before streams begin
- a common envelope for all telemetry and input stream messages
- host-driven screenshot control messages
- resumable or recoverable live sessions
- generic cancellation or progress messages for tool calls

Do not assume any of those messages or flows exist when interoperating with the
current client.

## Remote Tool Protocol

This document describes the remote-tool behavior currently implemented on top of
the Ansight pairing WebSocket. It is a wire contract: JSON envelopes, routing,
guard behavior, response shapes, and transfer handoff data.

In one sentence: the host sends a JSON request asking what tools exist or asking
one tool to run, and the client replies on the same WebSocket with either a
catalog, a result, or an error.

Tool execution is a protocol extension on the live pairing session. It does not
replace the control plane described in [Connection Protocol](#connection-protocol), and it does not
define its own WebSocket.

### What Remote Tools Are For

Remote tools exist so a paired host can ask the running target for explicit,
registered development capabilities. The protocol gives the host two basic
operations:

- discover what is available, including argument schemas, result schemas,
  scopes, and security metadata
- call one available tool and receive either a JSON result, a structured error,
  or a JSON handoff to a binary transfer

In practical use, a host or bridge first calls `tool.query`, chooses a tool from
the returned catalog, builds arguments from that tool's schema, and sends
`tool.call`. The returned data can then be correlated with the live session:
screenshots explain pixels, visual-tree data explains UI structure, telemetry
explains timing, logs explain runtime messages, and artifacts preserve
application-state snapshots that the host includes in the capture session.

The tool protocol does not make every connected target equally inspectable. The
catalog depends on the tools registered by the target and on the current guard.
That keeps the wire protocol generic while still letting each target expose only
the development surfaces it intentionally registered.

### Current Flow

The current flow is:

1. A pairing WebSocket session is already open.
2. The host sends `tool.query` or `tool.call` as a WebSocket text message.
3. The client receive loop detects the tool message before normal control
   response handling.
4. The client validates the envelope id and capability.
5. If the runtime is not initialized, the client replies with `tool.error`.
6. If the runtime is initialized, the client applies the current tool guard.
7. For `tool.query`, the client returns the visible catalog as `tool.catalog`.
8. For `tool.call`, the client executes one registered tool and returns
   `tool.result` or `tool.error`.
9. After a successful `tool.call` response is sent, any binary transfer queued
   for that request id starts on the same WebSocket.

Tool messages that are handled by this path are not forwarded into the normal
`CONTROL_RESP` acknowledgement queue.

### Message Detection

The current client treats an incoming text message as a tool-protocol request
only when all of these are true:

- the message parses as a JSON object
- `type` is a string equal to `tool.query` or `tool.call`
- `capability`, when present and non-empty, is `tool.exec`

If `type` is not `tool.query` or `tool.call`, the message is not handled by the
tool processor. If `capability` is a different non-empty string, the message is
also not handled by the tool processor.

If a message appears to be a tool request but cannot be parsed into a valid
envelope, the client replies with `tool.error` and code
`tool_protocol_invalid_request`.

### Envelope

All tool-protocol messages use this JSON envelope:

```json
{
  "type": "tool.query",
  "id": "req_1",
  "replyTo": null,
  "sessionId": "sess_1",
  "sentAt": "2026-03-20T10:00:00+00:00",
  "capability": "tool.exec",
  "payload": {}
}
```

Fields:

- `type`: message kind
- `id`: required request or response id
- `replyTo`: id of the request being answered, or `null`
- `sessionId`: optional logical session id
- `sentAt`: UTC timestamp for when the envelope was created
- `capability`: capability namespace; current value is `tool.exec`
- `payload`: message body

Current request parsing requires a non-empty `id`. Full manual processing also
requires a non-empty `type`. `sessionId`, `sentAt`, and `payload` are optional
for request parsing.

Current response envelope behavior:

- response `id` is `<requestId>.response`
- response `replyTo` is `<requestId>`
- response `sessionId` copies the request `sessionId`
- response `capability` is `tool.exec`
- response `sentAt` is generated when the response is created

When the request is malformed before a request id can be read, the current
client creates an error id in the form `tool.error.<32-lowercase-hex-guid>` and
leaves `replyTo` unset.

### Guard

The guard controls what the host can discover and execute.

Current guard fields in `tool.catalog`:

```json
{
  "discoveryEnabled": true,
  "executionEnabled": true,
  "allowedScopes": ["Read"]
}
```

Current scopes:

- `Read`: inspection or retrieval
- `Write`: create or update
- `Delete`: destructive removal

Current guard behavior:

- if discovery is disabled, `tool.query` returns `tool.error` with code
  `tool_discovery_disabled`
- tools outside `allowedScopes` are omitted from `tool.catalog`
- if execution is disabled, `tool.call` returns `tool_execution_denied`
- if a tool's scope is not allowed, `tool.call` returns
  `tool_execution_denied`

If no tools are registered, `tool.query` returns an empty catalog and
`tool.call` for any tool id returns `tool_not_found`.

### `tool.query`

`tool.query` asks for the visible tool catalog.

Example request:

```json
{
  "type": "tool.query",
  "id": "req_1",
  "sessionId": "sess_1",
  "capability": "tool.exec",
  "payload": {}
}
```

The current client does not require a particular `payload` shape for
`tool.query`.

Successful response type: `tool.catalog`

Response payload:

```json
{
  "guard": {
    "discoveryEnabled": true,
    "executionEnabled": true,
    "allowedScopes": ["Read"]
  },
  "tools": [
    {
      "id": "example.tool",
      "name": "Example Tool",
      "description": "Example",
      "category": "Diagnostics",
      "scope": "Read",
      "keywords": "example",
      "security": {
        "level": "High",
        "summary": "Reads and exports sensitive app data.",
        "implications": ["reads_app_data", "exports_data"]
      },
      "argumentsSchema": {},
      "resultSchema": {}
    }
  ],
  "count": 1
}
```

`count` is the number of entries in `tools`.

### Catalog Entries

Each catalog entry describes one registered tool:

- `id`: stable id used in `tool.call`
- `name`: human-readable name
- `description`: human-readable description
- `category`: grouping label
- `scope`: `Read`, `Write`, or `Delete`
- `keywords`: search keywords
- `security`: optional security metadata
- `argumentsSchema`: JSON-schema-like argument shape
- `resultSchema`: JSON-schema-like result shape

`security` appears only when the tool declares explicit security metadata. When
present, it contains:

- `level`: security sensitivity label
- `summary`: human-readable security summary
- `implications`: canonical implication strings with duplicates removed
  case-insensitively

`argumentsSchema` and `resultSchema` use a JSON-schema-like subset:

- `type`: one of `object`, `array`, `string`, `integer`, `number`, `boolean`,
  or an array of one of those plus `null`
- `description`: optional human-readable text
- `format`: optional string format hint
- `enum`: optional allowed string values
- `items`: item schema for arrays
- `properties`: object properties
- `required`: required object property names
- `additionalProperties`: whether undeclared object properties are allowed

The actual tool ids available in a session depend on which tools were
registered and which scopes the current guard allows.

### `tool.call`

`tool.call` asks the client to execute one registered tool.

Example request:

```json
{
  "type": "tool.call",
  "id": "req_2",
  "sessionId": "sess_1",
  "capability": "tool.exec",
  "payload": {
    "toolId": "example.tool",
    "arguments": {
      "limit": 10
    }
  }
}
```

Current request rules:

- `payload` must be a JSON object
- `payload.toolId` must be present and non-empty
- `payload.arguments` is optional
- `payload.arguments` is used only when it is a JSON object
- null argument values are skipped
- scalar argument values are converted to strings
- object and array argument values are converted to compact JSON strings
- argument keys are treated case-insensitively by the current execution path

The current client also injects these internal string arguments before tool
execution:

- `__ansight_requestId`: the `tool.call` request id
- `__ansight_sessionId`: the request `sessionId`, when present

Successful response type: `tool.result`

Response payload before optional compression:

```json
{
  "toolId": "example.tool",
  "success": true,
  "message": "ok",
  "result": {}
}
```

If the tool returns no message or no JSON result, `message` or `result` can be
`null`.

### Encoded Result Payloads

Large successful `tool.result` payloads can be compressed. The current client
does this only when the compact JSON response payload is at least 32768 bytes
and gzip plus base64 is smaller than the original JSON.

When compression is used, the envelope `payload` is replaced by this wrapper:

```json
{
  "$ansightEncoding": "gzip-base64-json",
  "contentType": "application/json",
  "originalByteCount": 65536,
  "compressedByteCount": 4096,
  "data": "base64-gzip-json"
}
```

To decode it:

1. Base64-decode `data`.
2. Gzip-decompress the bytes.
3. Parse the resulting UTF-8 JSON as the original `tool.result` payload.

Catalog and error payloads are not compressed by the current client.

### `tool.error`

`tool.error` is returned when the request is invalid, the runtime is not ready,
discovery is disabled, execution is denied, the tool is missing, or the tool
fails.

Error payload:

```json
{
  "code": "tool_not_found",
  "message": "Tool 'example.tool' is not registered.",
  "retryable": false,
  "details": null
}
```

Current error codes produced by the protocol bridge:

- `tool_protocol_invalid_request`
- `tool_protocol_unknown_type`
- `tool_runtime_not_initialized`
- `tool_discovery_disabled`
- `tool_call_payload_invalid`
- `tool_call_missing_id`
- `tool_not_found`
- `tool_execution_denied`
- `tool_execution_failed`
- `tool_execution_exception`

Tool implementations can return their own error codes. Those codes appear in a
`tool.error` envelope with `retryable = false`.

### JSON File Transfer Tool Results

Some registered tools keep file transfer inside JSON `tool.result` payloads.
The current `files.download_file` behavior is:

- reads at most `maxBytes` per call
- default `maxBytes` is 262144
- absolute maximum `maxBytes` is 1048576
- starts at `offsetBytes`, defaulting to `0`
- validates `expectedVersion` when supplied
- returns either UTF-8 text or base64 content
- returns a `nextRequest` object when more bytes remain

Representative result fields:

```json
{
  "rootAlias": "cache",
  "relativePath": "logs/app.log",
  "fileName": "app.log",
  "fileExtension": ".log",
  "mimeType": "text/plain",
  "sizeBytes": 4096,
  "lastModifiedUtc": "2026-03-20T10:00:00Z",
  "version": "4096:123456789",
  "offsetBytes": 0,
  "requestedMaxBytes": 262144,
  "bytesRead": 4096,
  "hasMore": false,
  "nextOffsetBytes": null,
  "capturedAtUtc": "2026-03-20T10:01:00Z",
  "contentType": "text",
  "encoding": "utf-8",
  "text": "log text",
  "base64": null,
  "nextRequest": null
}
```

When `hasMore` is `true`, `nextRequest` contains the next `tool.call` payload:

```json
{
  "toolId": "files.download_file",
  "arguments": {
    "root": "cache",
    "path": "logs/app.log",
    "offsetBytes": "4096",
    "maxBytes": "262144",
    "encoding": "utf8",
    "expectedVersion": "4096:123456789"
  }
}
```

### Binary Transfer Tool Results

Some tools return JSON metadata first and then stream bytes with the `ASFT`
binary frame format documented in [Connection Protocol](#connection-protocol). This is the path for
results that become bytes on the host side instead of being embedded inside
JSON.

Any tool can use this pattern when its successful result includes the common
handoff fields below. Current ASFT-producing tools include:

- `files.begin_binary_download`
- `artifacts.request`
- `ui.get_screenshot`

Common handoff fields:

```json
{
  "downloadId": "req_2",
  "transferId": "0123456789abcdef0123456789abcdef",
  "deliveryMode": "websocket_binary",
  "wireProtocol": "ansight.file-transfer.v1",
  "status": "queued",
  "chunkBytes": 65536,
  "capturedAtUtc": "2026-03-20T10:01:00+00:00"
}
```

Tool-specific metadata appears alongside those fields:

- `files.begin_binary_download` includes file metadata such as root alias,
  relative path, file name, MIME type, size, modification time, and version
- `artifacts.request` includes an `artifact` metadata object describing the
  application-state snapshot
- `ui.get_screenshot` includes screenshot metadata such as platform, capture
  time, image format, width, height, MIME type, file name, byte count, and
  annotation status

Current binary transfer behavior:

- `transferId` is 32 lowercase hex characters
- default `chunkBytes` is 65536
- minimum `chunkBytes` is 1024
- maximum `chunkBytes` is 524288
- if `downloadId` is omitted, the request id is used
- the transfer is queued while the tool executes
- after the `tool.result` response is sent, the client starts the queued
  transfer for that request id
- binary chunks, completion, and errors use `ASFT` frames on the same WebSocket

The host or bridge chooses any host-local temp path. The client sends ids,
metadata, and bytes; it does not know where the host writes the file.

Other tools can use ordinary JSON-only `tool.result` payloads. Their ids,
arguments, result shapes, scopes, and security metadata are described by the
catalog rather than by separate transport rules.

### Artifact Tools

Artifacts are snapshots of application state exposed through the remote-tool
plane so the host can include them in the capture session. The payload can be
JSON, text, binary, an archive, or an image; what makes it an artifact is that
the target created it as session evidence.

In practical use, a host discovers requestable artifacts with `artifacts.query`,
then asks for one snapshot with `artifacts.request`. The request returns
metadata immediately and queues an `ASFT` transfer for the payload bytes. A
host or bridge can then record the payload in the capture session and line it
up with nearby screenshots, logs, telemetry, visual-tree captures, touches, and
annotations.

Artifacts are different from ordinary JSON tool results:

- an ordinary tool result is best for small JSON answers or narrow live actions
- an artifact has stable metadata and a payload stream that can be materialized
  as a host-side file
- artifact tools are read-scoped, but requested artifacts can still contain
  sensitive target data

#### `artifacts.query`

`artifacts.query` asks the target for registered artifact providers and
currently requestable artifact definitions.

Arguments:

```json
{
  "providerId": "target.runtime-state",
  "category": "runtime",
  "kind": "snapshot",
  "tag": "state"
}
```

All arguments are optional:

- `providerId`: returns only the provider with this id
- `category`: returns only artifact definitions in this category
- `kind`: returns only artifact definitions with this kind
- `tag`: returns only artifact definitions containing this tag

Current matching is case-insensitive. Provider query failures do not fail the
whole tool call; the provider appears in `providers` with an `error` string and
the query continues.

Successful result object:

```json
{
  "providers": [
    {
      "id": "target.runtime-state",
      "name": "Runtime State",
      "description": "Exports selected runtime snapshots.",
      "category": "runtime",
      "tags": ["state"],
      "metadata": {
        "owner": "debug"
      },
      "error": null
    }
  ],
  "artifacts": [
    {
      "providerId": "target.runtime-state",
      "id": "current-state",
      "name": "Current State",
      "description": "Exports the current runtime state.",
      "kind": "snapshot",
      "category": "runtime",
      "tags": ["state", "json"],
      "metadata": {
        "scope": "current"
      },
      "content": {
        "supportedMimeTypes": ["application/json"],
        "defaultMimeType": "application/json",
        "suggestedFileName": "current-state.json",
        "supportsText": true,
        "supportsBinary": false,
        "sizeKnownBeforeCreation": false,
        "estimatedSizeBytes": null
      },
      "argumentsSchema": {
        "type": "object",
        "additionalProperties": true
      },
      "security": {
        "level": "High",
        "summary": "Exports selected runtime state.",
        "implications": ["exports_data"]
      }
    }
  ],
  "providerCount": 1,
  "artifactCount": 1,
  "capturedAtUtc": "2026-03-20T10:01:00+00:00"
}
```

Provider fields:

- `id`: stable provider id used in requests
- `name`: human-readable provider name
- `description`: provider description
- `category`: grouping value for clients
- `tags`: provider grouping or search tags
- `metadata`: string key/value metadata
- `error`: provider query error, or `null`

Artifact definition fields:

- `providerId`: provider id that owns the artifact definition
- `id`: stable artifact id within the provider
- `name`: human-readable artifact name
- `description`: artifact description
- `kind`: provider-defined artifact kind, such as `log`, `trace`, `report`,
  `image`, or `snapshot`
- `category`: grouping value for clients
- `tags`: artifact grouping or search tags
- `metadata`: string key/value metadata
- `content`: content descriptor
- `argumentsSchema`: provider-specific request argument schema
- `security`: security metadata with `level`, `summary`, and `implications`

Content descriptor fields:

- `supportedMimeTypes`: MIME types the artifact can produce
- `defaultMimeType`: preferred MIME type, or `null`
- `suggestedFileName`: suggested file name, or `null`
- `supportsText`: whether the artifact can produce text content
- `supportsBinary`: whether the artifact can produce binary content
- `sizeKnownBeforeCreation`: whether the size is known before request time
- `estimatedSizeBytes`: best-effort size estimate, or `null`

#### `artifacts.request`

`artifacts.request` asks one provider to create one artifact snapshot and stream
its payload to the host.

Arguments:

```json
{
  "providerId": "target.runtime-state",
  "artifactId": "current-state",
  "downloadId": "state_1",
  "chunkBytes": 65536,
  "arguments": {
    "includeCache": true
  }
}
```

Required arguments:

- `providerId`: provider id returned by `artifacts.query`
- `artifactId`: artifact id returned by `artifacts.query`

Optional arguments:

- `downloadId`: caller correlation id for the host-side artifact file; when
  omitted, the tool request id is used
- `chunkBytes`: maximum payload bytes per `ASFT` chunk frame
- `arguments`: provider-specific request arguments

Current `chunkBytes` limits match other ASFT producers:

- default: 65536
- minimum: 1024
- maximum: 524288

Successful result object:

```json
{
  "artifact": {
    "artifactId": "current-state",
    "providerId": "target.runtime-state",
    "name": "Current State",
    "kind": "snapshot",
    "description": "Runtime state captured from the target.",
    "mimeType": "application/json",
    "fileName": "current-state.json",
    "sizeBytes": 4096,
    "createdAtUtc": "2026-03-20T10:01:00.0000000+00:00",
    "tags": ["state", "json"],
    "metadata": {
      "scope": "current"
    }
  },
  "downloadId": "state_1",
  "transferId": "0123456789abcdef0123456789abcdef",
  "deliveryMode": "websocket_binary",
  "wireProtocol": "ansight.file-transfer.v1",
  "status": "queued",
  "chunkBytes": 65536,
  "capturedAtUtc": "2026-03-20T10:01:00.0000000+00:00"
}
```

Artifact metadata fields:

- `artifactId`: requested artifact id
- `providerId`: requested provider id
- `name`: human-readable snapshot name
- `kind`: provider-defined artifact kind
- `description`: artifact description, or `null`
- `mimeType`: MIME type of the payload
- `fileName`: suggested file name for materializing the payload
- `sizeBytes`: payload byte size, or `null`
- `createdAtUtc`: UTC timestamp when the snapshot was created
- `tags`: snapshot grouping or search tags
- `metadata`: string key/value metadata

Request behavior:

- artifact requests require a live tool request id, initialized runtime, and
  active binary transfer channel
- the requested provider id and artifact id must both be present
- the provider id must be registered
- returned metadata `providerId` and `artifactId` must match the request,
  compared case-insensitively
- returned metadata must have non-empty `name`, `kind`, `mimeType`, and
  `fileName`
- if metadata `sizeBytes` is `null` and the payload source knows its size, the
  result uses the payload size
- after the `tool.result` is sent, the queued payload stream starts as `ASFT`
  frames with the returned `transferId`

Current artifact-specific failure codes returned by tool implementations:

- `artifact_request_unavailable`
- `artifact_request_missing_provider_id`
- `artifact_request_missing_artifact_id`
- `artifact_provider_not_found`
- `artifact_transfer_unavailable`
- `artifact_request_failed`

### Out of Scope

The current tool protocol does not define:

- cancellation messages
- progress events
- generic transport-level chunked tool results
- request correlation beyond `id` and `replyTo`
- a separate capability namespace per tool family
- host-local file paths or host temp-directory decisions
- a host-side artifact database or session archive schema
- live periodic screenshot transport through `tool.result`

Live periodic screenshots use the separate `ASJP` screenshot stream described
in [Connection Protocol](#connection-protocol). Tool-requested screenshots can
use the `ASFT` handoff pattern described above.
