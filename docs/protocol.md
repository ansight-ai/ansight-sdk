# Ansight Protocol

This document describes the current Ansight enrollment, connection, session,
telemetry, and remote-tool wire contracts. There is one supported enrollment
protocol. Older pairing handshakes and pairing-config formats are not accepted.

## Enrollment and connection

### Design goals

The default developer flow is deliberately small:

1. The app initializes and activates the SDK.
2. A host-local runtime registers automatically with a running, signed-in
   host through loopback.
3. host shows one generic, short-lived, one-use enrollment QR.
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
      "maxToolPolicy": "read"
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

`appId: "*"` is the v2 any-app target. The host uses it for the generic QR.
The invite does not pre-register an app and the SDK never sends `*` as its
runtime identity. The scanning SDK supplies its real package or bundle id in
`ENROLLMENT_CONNECT`; host creates the app record after successful
authorization. Existing app-specific v2 invites retain their exact app-id
check.

### Installation identity

Each SDK generates a random, stable `deviceId` and local enrollment token in
app-private storage. It does not use a hardware identifier and does not require
a platform permission.

For host-local runtimes, host creates or reuses a local grant keyed by the
app id, installation id, and token. Local enrollment is accepted only when the
UDP request originated from loopback and host has an authenticated account.
The SDK checks its well-known installed and source-build host ports with
short loopback-only timeouts before trying older stored registrations. It
retries while active, so host does not need to be running during the app
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
`processSessionId`, open host/offline session identifiers, recent
breadcrumbs, and OS termination diagnostics.

When the host is connected, the recovering process sends an acknowledged
`crash.handoff` control request:

```json
{
  "type": "CONTROL_REQ",
  "id": "request_crash_1",
  "action": "crash.handoff",
  "payload": {
    "reportId": "6f914d7bdca928cfbf6e9691aefa0a42",
    "targetProcessSessionId": "process_that_crashed",
    "targetSessionId": "previous_host_session_if_known",
    "deliveryProcessSessionId": "recovering_process",
    "report": {
      "schema": "ansight.crash.v1",
      "reportId": "6f914d7bdca928cfbf6e9691aefa0a42",
      "previousProcessSessionId": "process_that_crashed",
      "platform": "android",
      "kind": "native_crash",
      "confidence": "confirmed",
      "occurredAtUtc": "2026-08-13T00:00:00.000Z",
      "candidate": {},
      "termination": {},
      "breadcrumbs": [],
      "traceBase64": "dHJhY2U="
    }
  }
}
```

The host uses the previous process identity and `reportId` as an idempotency key and associates the report with
`targetSessionId` or `targetProcessSessionId`. It returns a successful
`CONTROL_RESP` only after the report is durably stored. The SDK then marks the
The host delivery complete. Failed and interrupted requests remain in the
bounded outbox for a later connection.

If an offline capture was open when the process died, `.NET` recovery writes
the same report under `diagnostics/crashes` in that prior capture, seals its
manifest with the crash termination kind, and acknowledges the offline copy.
The normal offline ZIP/upload path consequently transports the crash without
network work in the fatal handler. A report is removed only after every
delivery route required by its prior-session associations has succeeded.

The receiving host validates report/envelope identities and the target app registration.
It retains the report and decoded native trace or MetricKit payload as a `crash-report`
session artifact. If the original session is unavailable, the delivering session retains
it with the previous process identity in `report.json`. Reports are limited to 8 MiB
of serialized JSON and each decoded trace to 4 MiB. Malformed or oversized reports
are rejected without acknowledgement.

The SDK option is `hostHandoffEnabled` (`HostHandoffEnabled` in .NET).
It defaults to `true`; set it to `false` to disable delivery to the host.

### Telemetry streams

Telemetry is sent as WebSocket text messages:

- `CLIENT_METRIC_CHANNELS` describes channel metadata.
- `CLIENT_METRICS` carries timestamped numeric samples.
- `CLIENT_EVENTS` carries timestamped events.
- `CLIENT_TOUCH_INPUT` carries `ansight.touches.v1` packed touch batches.
- `CLIENT_NETWORK_REQUEST` carries one `ansight.network-request.v1` HTTP metadata record.
- `CLIENT_VISUAL_TREE` carries screenshot-aligned or touch-triggered visual-tree snapshots.

Network request messages use this shape:

```json
{
  "type": "CLIENT_NETWORK_REQUEST",
  "sentAtUtc": "2026-08-23T00:00:00.125Z",
  "request": {
    "schema": "ansight.network-request.v1",
    "id": "0198...",
    "source": "dotnet.httpclient",
    "startedAtUtc": "2026-08-23T00:00:00Z",
    "completedAtUtc": "2026-08-23T00:00:00.125Z",
    "durationMilliseconds": 125,
    "method": "GET",
    "url": "https://api.example.test/orders?token=%3Credacted%3E",
    "protocol": "2.0",
    "requestHeaders": [{ "name": "Authorization", "value": "<redacted>" }],
    "requestBodySizeBytes": 128,
    "statusCode": 200,
    "reasonPhrase": "OK",
    "responseHeaders": [{ "name": "Content-Type", "value": "application/json" }],
    "responseBodySizeBytes": 512,
    "errorType": null,
    "errorMessage": null
  }
}
```

V1 carries metadata only: clients never read or send HTTP bodies. Framework
integrations apply app-configurable sanitizers before crossing their runtime
bridge. Android and iOS decode the bridge payload into typed native models and
apply mandatory redaction and bounds immediately before transport; the host
sanitizes again before persistence. The .NET implementation applies the same
policy in its managed runtime for `HttpClient` and offline capture paths.
Credential-bearing headers, URL user information, and sensitive query values
are redacted. The host stores each record as an individual JSON file under
`network/requests/` in local captures and portable archives.

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
  "gesturePhase": "ended",
  "touchAction": "up",
  "touchCapturedAtUtc": "2026-08-13T00:00:00.120Z",
  "payload": {
    "captureTrigger": {
      "kind": "touch",
      "gestureId": "gesture-37f5...",
      "gesturePhase": "ended",
      "touchAction": "up",
      "touchCapturedAtUtc": "2026-08-13T00:00:00.120Z"
    }
  }
}
```

Current SDKs capture only on touch down and touch up. The first down in a
gesture uses phase `started`, the final up uses `ended`, and additional pointer
downs or non-terminal pointer ups use `checkpoint`. Move and cancel events do
not trigger visual-tree capture. Touch-tree delivery is best-effort: SDKs keep
at most one pending trigger and coalesce rapid boundaries, preferring a gesture
start over terminal or checkpoint snapshots. This bounds UI-thread work and
protects the configured screenshot cadence.

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
  "id": "tool_query_1",
  "payload": {
    "detail": "index",
    "ifRevision": "sha256:known-catalog-revision",
    "ifAvailabilityRevision": "sha256:known-availability-revision"
  }
}
```

Catalog schema version 3 separates the static tool index from runtime
availability. `detail: "index"` returns ids, names, descriptions, category,
ordered `policy`, explicit prerequisite ids, and a per-tool
`definitionRevision` without argument or result schemas. Hosts fetch schemas
only for selected ids with `detail: "definitions"` and an `ids` array. A
legacy request without `detail` returns full definitions.

The static `revision` changes only when the visible definitions or guard
change. `availabilityRevision` tracks runtime executability independently.
When both supplied revisions match, the response contains only `schema`,
`revision`, and `unchanged: true`. When only availability changed, `changes`
is a replacement snapshot containing only non-default availability entries;
all omitted tools default to available and executable. One envelope-level
`evaluatedAtUtc` timestamps the snapshot.

Queries may also include `ids`, `query`, `feature`, `policy`,
`executableOnly`, and `limit`/`maxResults`. Filtering is applied in the SDK
before serialization. `policy` is one of `read`, `write`, or `critical`.
JSON argument encoding, available/executable state, empty keywords, and
`additionalProperties: false` are protocol defaults and are omitted, as are
`unchanged: false` and the default `detail: "full"`. Definition projections
omit availability, timestamp, category, and total-count fields already held
with the index. Category
counts replace the older capability manifest's repeated tool-id lists.

Large catalog, result, batch, and error payloads use `gzip-base64-json` when
the complete encoded wrapper is smaller than the original JSON. The wrapper
reports `originalByteCount` and `compressedByteCount` for host metrics.

### Call

```json
{
  "type": "tool.call",
  "id": "tool_call_1",
  "payload": {
    "toolId": "ui.perform_action",
    "arguments": {
      "reference": {
        "source": "native",
        "snapshotId": "native:42:...",
        "revision": 42,
        "nodeId": "window.0.child.3"
      },
      "action": "tap"
    },
    "after": {
      "include": ["visualTree", "screenshot"],
      "delayMilliseconds": 100
    }
  }
}
```

The response uses `replyTo` to correlate the request and carries a success flag
and either a result or a structured error. `after` composes act and verify in
one round trip. It supports `visualTree`, `screenshot`, a delay from 0 through
2000 milliseconds, and tool-specific evidence arguments. Large binary results
may be transferred through the `ASFT` stream and referenced by transfer id.

Tools that implement the native JSON contract receive JSON objects directly
and validate input and output schemas. Older flattened-string tools remain
compatible and advertise `argumentEncoding: "flattened-string"` in the
catalog; native JSON tools advertise `argumentEncoding: "json"`.

### Batch

`tool.batch` executes between 1 and 32 calls sequentially. Each call may have a
caller-defined `callId`, arguments, and an `after` evidence request.

```json
{
  "type": "tool.batch",
  "id": "checkout_workflow_1",
  "payload": {
    "continueOnError": false,
    "calls": [
      {
        "callId": "find-save",
        "toolId": "ui.query_nodes",
        "arguments": { "automationId": "save" }
      },
      {
        "callId": "tap-save",
        "toolId": "ui.perform_action",
        "arguments": {
          "reference": {
            "source": "native",
            "snapshotId": "native:42:...",
            "revision": 42,
            "nodeId": "window.0.child.3"
          },
          "action": "tap"
        },
        "after": { "include": ["visualTree"] }
      }
    ]
  }
}
```

The app answers with `tool.batch.result`. Results retain input order and include
`index`, `callId`, `toolId`, success/error state, and any evidence. Unless
`continueOnError` is true, execution stops after the first error.

### Generic UI contract

Every visual-tree SDK exposes these framework-neutral operations:

- `ui.get_visual_tree` captures a source and returns `source`, `snapshotId`,
  monotonically increasing `revision`, and `nodeIdentity` metadata.
- `ui.query_nodes` captures or reuses a snapshot, filters nodes by stable
  semantic fields, and returns a complete `reference` for every match.
- `ui.inspect_node` accepts a node reference and returns the same identity
  fields with node details.
- `ui.perform_action` accepts a reference and an action such as `tap`, `focus`,
  `unfocus`, `setValue`, `typeText`, or `toggle`, depending on the provider.
- `ui.wait` repeatedly captures and queries until `exists`, `notExists`,
  `visible`, or `enabled` is satisfied or the timeout expires.

`source` selects the provider: `native`, `maui`, `react`, `flutter`, or `dom`
where installed. A reference is snapshot-scoped. Once a newer snapshot is
captured for that source, operations on an older reference fail with
`stale_node_reference` and `refreshWith: "ui.query_nodes"`. This makes race
conditions explicit instead of silently acting on a reused framework id.

### Artifact Tools

`artifacts.query` returns the app-provided artifact catalog.
`artifacts.request` creates one catalog artifact and returns inline text,
inline bytes, or an `ASFT` transfer reference. Both use the `read` policy and remain
subject to the local tool guard.

### Guard

The client enforces the configured tool guard before invoking a tool:

- disabled
- read-only
- read-write
- full access

Each preset maps to a single maximum policy: `read-only` permits `read`,
`read-write` permits `read` and `write`, and `full access` permits all three.
Use `critical` for destructive operations, secret access, and arbitrary app
code invocation. This one ordered value replaces the former scope, security
level, and implication fields.

The enrollment invite's `maxToolPolicy` can further restrict access. The host
cannot raise the maximum beyond the app's local configuration.

## Operational behavior

- Enrollment invites are bearer secrets. Do not publish, log, or ship them in
  production resources.
- Local enrollment is rejected unless the datagram source is loopback and
  host is authenticated.
- The host consumes first registration atomically.
- A registered installation reconnects with its saved state and does not need
  the original QR to remain unexpired.
- The WebSocket token is ephemeral and issued per accepted UDP request.
- Cellular connections are disabled by default by SDK policy.
- Clear-text transport is a deliberate low-friction development trade-off;
  use only on a network you trust.
