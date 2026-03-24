# Ansight Connection Protocol

This document describes the protocol behavior currently implemented by the `.NET` SDK under [src/dotnet/Ansight](../../src/dotnet/Ansight).

It is intentionally descriptive rather than aspirational. If this file and the SDK disagree, the SDK is the source of truth and this document should be updated.

## Roles

- `app client`: the SDK running inside the target app
- `host`: the machine accepting the pairing session

The current `.NET` SDK always behaves as the initiating client.

## Current connection flow

The implemented session flow is:

1. Parse and validate a pairing document.
2. Resolve a host IP address from `PairingConnectionOptions.ManualHostAddress`.
3. Send a UDP `CONNECT_REQ` JSON packet to the host discovery port.
4. Receive a UDP `CONNECT_RESP` JSON packet with a WebSocket handoff.
5. Open a WebSocket using the returned `token` query string.
6. Wait for one initial text frame from the host.
7. Attach the WebSocket transport, send an initial `DeviceAppProfile`, and start optional screenshot streaming.

The relevant code paths are:

- [PairingConfigDocumentService.cs](../../src/dotnet/Ansight/Pairing/PairingConfigDocumentService.cs)
- [PairingSessionConnector.cs](../../src/dotnet/Ansight/Pairing/PairingSessionConnector.cs)
- [PairingSessionTransport.cs](../../src/dotnet/Ansight/Pairing/PairingSessionTransport.cs)
- [PairingSessionClient.cs](../../src/dotnet/Ansight/Pairing/PairingSessionClient.cs)

## Default constants

The SDK exposes these defaults in [PairingProtocolDefaults.cs](../../src/dotnet/Ansight/Pairing/PairingProtocolDefaults.cs):

- discovery UDP port: `45123`
- WebSocket port: `45124`
- WebSocket path: `/ws`

Only the discovery port is directly consumed by the current client. For the WebSocket handoff, the client requires the host to return `webSocketPort`, `webSocketPath`, and `webSocketToken` in the accepted UDP response.

## Pairing documents

The SDK accepts either a plain `PairingConfig` or a bootstrap wrapper document.

### `PairingConfig`

The base config is defined by:

- [PairingConfig.cs](../../src/dotnet/Ansight/Pairing/Models/PairingConfig.cs)
- [PairingHost.cs](../../src/dotnet/Ansight/Pairing/Models/PairingHost.cs)
- [PairingChallenge.cs](../../src/dotnet/Ansight/Pairing/Models/PairingChallenge.cs)
- [PairingTrust.cs](../../src/dotnet/Ansight/Pairing/Models/PairingTrust.cs)

Important fields are:

- `schema`
- `configId`
- `appId`
- `appName`
- `issuedAt`
- `expiresAt`
- `oneTimeToken`
- `host`
- `challenge`
- `trust`
- `signature`

### Bootstrap and QR payloads

The SDK also parses:

- `ansight.pairing-bootstrap.v1` via [PairingBootstrapDocument.cs](../../src/dotnet/Ansight/Pairing/Models/PairingBootstrapDocument.cs)
- `ansight.pairing-connection-hint.v1` via [PairingConnectionHint.cs](../../src/dotnet/Ansight/Pairing/Models/PairingConnectionHint.cs)
- `ansight.discovery-hint.v1` via [PairingDiscoveryHint.cs](../../src/dotnet/Ansight/Pairing/Models/PairingDiscoveryHint.cs)
- `ansight.qr-pairing-connection.v1` via [PairingQrConnectionPayload.cs](../../src/dotnet/Ansight/Pairing/Models/PairingQrConnectionPayload.cs)

When a bootstrap document includes a `connectionHint`, the parser applies the hint's `configId`, `issuedAt`, `expiresAt`, `oneTimeToken`, and `challenge` into the effective config, but signature verification still uses the original `pairingConfig` as the trust anchor.

## Validation and trust

Before opening a session, the client validates:

- the pairing config signature
- the config expiry time
- the `appId`, when the caller supplies an expected app id

Signature verification uses the host public key embedded in the config and currently accepts several historical canonical JSON forms for compatibility. That behavior lives in [PairingConfigDocumentService.cs](../../src/dotnet/Ansight/Pairing/PairingConfigDocumentService.cs) and [PairingCanonicalJson.cs](../../src/dotnet/Ansight/Pairing/PairingCanonicalJson.cs).

Important current limitation: the UDP connect request, UDP connect response, and WebSocket hello are not separately signed or schema-validated beyond normal JSON parsing and source-address checks.

## Host address resolution

The current `.NET` connector is manual-host driven.

- `PairingConnectionOptions.DiscoveryMode` supports `ConfiguredHint` and `BasicManual`
- both modes still require `PairingConnectionOptions.ManualHostAddress` to be a valid IP address
- `ParsedPairingDocument.DiscoveryHint` is parsed and preserved, but the connector does not currently resolve or probe it automatically
- there is no LAN discovery implementation in the current base `.NET` SDK

This means the progress message may say `Using configured host hint`, but the connector still uses the explicit `ManualHostAddress` value.

## UDP connect handoff

The client sends a JSON packet with camel-cased property names using [PairingJson.cs](../../src/dotnet/Ansight/Pairing/PairingJson.cs).

### Connect request

Message type: `CONNECT_REQ`

JSON shape:

```json
{
  "type": "CONNECT_REQ",
  "ver": 1,
  "configId": "cfg_123",
  "oneTimeToken": "token_123",
  "appId": "com.example.app",
  "clientName": "My App"
}
```

Model: [ConnectRequest.cs](../../src/dotnet/Ansight/Pairing/Models/ConnectRequest.cs)

### Connect response

The client accepts only responses:

- received from the selected host IP address
- whose `type` is exactly `CONNECT_RESP`

JSON shape:

```json
{
  "type": "CONNECT_RESP",
  "ver": 1,
  "accepted": true,
  "reason": "ok",
  "reasonMessage": null,
  "hostId": "host_123",
  "hostName": "dev-machine",
  "message": "ready",
  "webSocketPort": 45124,
  "webSocketPath": "/ws",
  "webSocketToken": "ws_token"
}
```

Model: [ConnectResponse.cs](../../src/dotnet/Ansight/Pairing/Models/ConnectResponse.cs)

If `accepted` is `true` but any of `webSocketPort`, `webSocketPath`, or `webSocketToken` is missing, the client fails the connection attempt.

## WebSocket session

The client opens:

```text
ws://<host-address>:<webSocketPort><webSocketPath>?token=<webSocketToken>
```

Current connector behavior:

- up to `12` connection attempts
- each WebSocket connect attempt has a `2s` timeout
- retries wait `250ms`
- after connect, the client waits up to `10s` for one initial host text frame

That first host frame is treated as an opaque hello string. The client reports it, but does not parse it into a structured schema.

## WebSocket receive and acknowledgement model

The transport implementation is in [PairingSessionTransport.cs](../../src/dotnet/Ansight/Pairing/PairingSessionTransport.cs).

Current behavior:

- outgoing text and binary writes are serialized through a send lock
- request/response style sends use `SendRequestAsync(...)`
- `SendRequestAsync(...)` waits for the next inbound non-tool text frame as the acknowledgement
- there is no request id correlation for these acknowledgements
- WebSocket close frames are surfaced internally as the sentinel string `<close>`

The receive pump is text-oriented. The implemented host-to-client control path assumes text frames, not binary command frames.

## Initial profile exchange

After the WebSocket is attached, [PairingSessionClient.cs](../../src/dotnet/Ansight/Pairing/PairingSessionClient.cs) sends a baseline `DeviceAppProfile` if one is available.

Message type: `DeviceAppProfile`

Model: [DeviceAppProfile.cs](../../src/dotnet/Ansight/Pairing/Models/DeviceAppProfile.cs)

Important top-level fields:

- `type = "DeviceAppProfile"`
- `schema = "ansight.device-app-profile.v1"`
- `sentAt`
- `reasonCode`
- `profileSeq`
- `device`
- `app`
- `runtime`
- `graphics`
- `permissions`
- `tags`

This message is sent with request/ack semantics, so the client waits for one inbound text acknowledgement after sending it.

## Telemetry message families

Telemetry streaming is implemented in [PairingTelemetryStreamer.cs](../../src/dotnet/Ansight/TelemetryStreaming/PairingTelemetryStreamer.cs).

### `CLIENT_LOG`

Sent by [PairingSessionClient.cs](../../src/dotnet/Ansight/Pairing/PairingSessionClient.cs) with request/ack semantics.

```json
{
  "source": "client",
  "type": "CLIENT_LOG",
  "sentAtUtc": "2026-03-20T10:00:00Z",
  "data": "log line"
}
```

### `CLIENT_DONE`

Sent by [PairingSessionClient.cs](../../src/dotnet/Ansight/Pairing/PairingSessionClient.cs) with request/ack semantics. After the send completes, the client closes the session transport.

```json
{
  "source": "client",
  "type": "CLIENT_DONE",
  "sentAtUtc": "2026-03-20T10:00:00Z",
  "data": "client log stream complete"
}
```

### `CLIENT_METRIC_CHANNELS`

Sent fire-and-forget with `SendTextAsync(...)`. No acknowledgement is expected.

```json
{
  "source": "client",
  "type": "CLIENT_METRIC_CHANNELS",
  "sentAtUtc": "2026-03-20T10:00:00Z",
  "channels": [
    {
      "id": 42,
      "name": "render",
      "color": "#ff8800"
    }
  ]
}
```

Only previously unseen channel ids are announced.

### `CLIENT_METRICS`

Sent fire-and-forget with `SendTextAsync(...)`. No acknowledgement is expected.

```json
{
  "source": "client",
  "type": "CLIENT_METRICS",
  "sentAtUtc": "2026-03-20T10:00:00Z",
  "metrics": [
    {
      "channel": 42,
      "value": 123.0,
      "capturedAtUtc": "2026-03-20T09:59:58Z"
    }
  ]
}
```

Current batching behavior:

- maximum `160` metrics per batch
- pending metric queue capped at `2000`

### `CLIENT_EVENTS`

Sent with request/ack semantics.

```json
{
  "source": "client",
  "type": "CLIENT_EVENTS",
  "sentAtUtc": "2026-03-20T10:00:00Z",
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

Current batching behavior:

- maximum `160` events per batch
- events are deduplicated by `id` while pending
- built-in event types are sent as their enum names, for example `Navigation`, `Lifecycle`, or `ScreenViewed`

## Tool protocol on the session socket

The transport automatically intercepts inbound remote-tool requests before they reach the normal acknowledgement queue.

Current supported inbound request types:

- `tool.query`
- `tool.call`

Current outbound response types:

- `tool.catalog`
- `tool.result`
- `tool.error`

The implementation lives in:

- [PairingToolProtocolProcessor.cs](../../src/dotnet/Ansight/Pairing/PairingToolProtocolProcessor.cs)
- [ToolProtocolBridge.cs](../../src/dotnet/Ansight/Tools/ToolProtocolBridge.cs)

The wire format and behavior are documented in [tools.md](tools.md).

## Binary file transfer stream

The `.NET` SDK now supports bridge-oriented binary file transfer over the live pairing WebSocket.

The control plane is a normal tool call:

- the host sends `tool.call` for `files.begin_binary_download`
- the SDK replies with `tool.result` containing `downloadId`, `transferId`, file metadata, `deliveryMode = websocket_binary`, and `wireProtocol = ansight.file-transfer.v1`
- after that `tool.result` has been sent, the SDK begins emitting binary WebSocket frames for that transfer

Important boundary:

- the app SDK never chooses or knows the host temp directory
- the MCP bridge or host is responsible for mapping `downloadId` / `transferId` to a local temp file path and writing the binary frames there

The implementation lives in:

- [BeginBinaryDownloadTool.cs](../../src/dotnet/Ansight.Tools.FileSystem/BeginBinaryDownloadTool.cs)
- [BinaryFileDownloadManager.cs](../../src/dotnet/Ansight.Host/BinaryFileDownloadManager.cs)
- [PairingBinaryTransferHub.cs](../../src/dotnet/Ansight/Pairing/PairingBinaryTransferHub.cs)
- [PairingFileTransferWireProtocol.cs](../../src/dotnet/Ansight/Pairing/PairingFileTransferWireProtocol.cs)

### Binary frame format

Each binary WebSocket message is:

1. a `56` byte header
2. zero or more payload bytes

Header layout:

- bytes `0..3`: ASCII magic `ASFT`
- byte `4`: protocol version, currently `1`
- byte `5`: frame type
- bytes `6..7`: reserved, currently `0`
- bytes `8..39`: `transferId` as `32` lowercase ASCII hex characters
- bytes `40..43`: sequence number, little-endian `Int32`
- bytes `44..51`: file offset for this frame, little-endian `Int64`
- bytes `52..55`: payload byte count, little-endian `Int32`

Frame types:

- `1`: chunk
- `2`: complete
- `3`: error

## Screenshot binary stream

If the runtime is initialized and `Options.SessionJpegCapture` is configured, the client starts automatic screenshot streaming after the session opens.

The implementation lives in:

- [PairingSessionJpegStreamer.cs](../../src/dotnet/Ansight/Screenshot/PairingSessionJpegStreamer.cs)
- [SessionJpegWireProtocol.cs](../../src/dotnet/Ansight/Screenshot/SessionJpegWireProtocol.cs)

Current behavior:

- screenshots are captured from the app's own root surface
- capture is client-driven and backpressure-driven; the next interval does not start until the previous frame has finished sending
- there is no host negotiation or host control message for this feature
- each WebSocket binary message contains one screenshot payload and may be fragmented at the WebSocket frame layer

### Binary message format

Each binary WebSocket message is:

1. a `28` byte header
2. JPEG bytes

Header layout:

- bytes `0..3`: ASCII magic `ASJP`
- byte `4`: protocol version, currently `1`
- byte `5`: image format, currently `1` for JPEG
- byte `6`: JPEG quality
- byte `7`: reserved, currently `0`
- bytes `8..15`: capture time as Unix milliseconds, little-endian `Int64`
- bytes `16..19`: width, little-endian `Int32`
- bytes `20..23`: height, little-endian `Int32`
- bytes `24..27`: JPEG byte count, little-endian `Int32`

## What is not implemented in the current `.NET` SDK

The current codebase does not implement these features, even if older docs or proposals mention them:

- automatic LAN or multicast discovery in the base pairing connector
- a signed discovery response format
- a signed UDP connect request or connect response
- a structured `host_hello` or `session_ready` envelope
- negotiated capability exchange before telemetry starts
- a common envelope for all non-tool WebSocket messages
- host-driven screenshot control messages
- resumable or recoverable pairing sessions

## Summary

The implemented protocol today is:

- signed pairing document validation
- manual host IP selection
- UDP `CONNECT_REQ` / `CONNECT_RESP` handoff
- WebSocket upgrade with an opaque host hello
- request/ack text messages for profile, logs, done, and event batches
- fire-and-forget text messages for metric channels and metric batches
- binary file transfer initiated by `files.begin_binary_download` using the `ASFT` frame header
- optional binary screenshot streaming using the `ASJP` frame header
- automatic `tool.query` / `tool.call` handling on the live session socket
