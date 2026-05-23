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
2. Resolve a host IP address from the pairing ticket discovery hint or `PairingConnectionOptions.HostAddressOverride`.
3. Send a UDP `CONNECT_REQ` JSON packet to the host discovery port.
4. Receive a UDP `CONNECT_RESP` JSON packet with a WebSocket handoff.
5. Open a WebSocket using the returned `token` query string.
6. Attach the WebSocket transport, send control requests for `session.open` and `device.profile`, then start optional screenshot and input-capture streaming.

The relevant code paths are:

- [PairingConfigDocumentService.cs](../../src/dotnet/Ansight.Core/Pairing/PairingConfigDocumentService.cs)
- [PairingSessionConnector.cs](../../src/dotnet/Ansight.Core/Pairing/PairingSessionConnector.cs)
- [PairingSessionTransport.cs](../../src/dotnet/Ansight.Core/Pairing/PairingSessionTransport.cs)
- [PairingSessionClient.cs](../../src/dotnet/Ansight.Core/Pairing/PairingSessionClient.cs)

## Remembered connection profiles

Remembered host connection profiles are SDK cache behavior, not an additional wire-protocol message. After a successful session, the `.NET` SDK stores a cached pairing document keyed by the Wi-Fi network name reported in `CONNECT_RESP.hostWifiName`. The cached profile stores the latest connected host address, host name, discovery port, discovery capture time, and signed pairing config for that network.

Profiles expire after 14 days by default, configurable through `WithHostConnectionProfileRetention(...)`. A successful reconnect to a host on the same reported Wi-Fi network refreshes the expiry timer and replaces the stored host/LAN metadata. During remembered-profile auto-connect, the SDK loads all valid remembered profiles and attempts them newest-first.

## Default constants

The SDK exposes these defaults in [PairingProtocolDefaults.cs](../../src/dotnet/Ansight.Core/Pairing/PairingProtocolDefaults.cs):

- discovery UDP port: `45123`
- WebSocket port: `45124`
- WebSocket path: `/ws`

Only the discovery port is directly consumed by the current client. For the WebSocket handoff, the client requires the host to return `webSocketPort`, `webSocketPath`, and `webSocketToken` in the accepted UDP response.

## Pairing documents

The app-facing SDK flow accepts pairing tickets or compact pairing ticket codes. The low-level client also accepts a plain `PairingConfig` as a direct input.

### `PairingConfig`

The base config is defined by:

- [PairingConfig.cs](../../src/dotnet/Ansight.Core/Pairing/Models/PairingConfig.cs)
- [PairingHost.cs](../../src/dotnet/Ansight.Core/Pairing/Models/PairingHost.cs)
- [PairingChallenge.cs](../../src/dotnet/Ansight.Core/Pairing/Models/PairingChallenge.cs)
- [PairingTrust.cs](../../src/dotnet/Ansight.Core/Pairing/Models/PairingTrust.cs)

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

### Pairing tickets

The ticket wrapper is defined by:

- [PairingTicket.cs](../../src/dotnet/Ansight.Core/Pairing/Models/PairingTicket.cs)
- [PairingTicketJson.cs](../../src/dotnet/Ansight.Core/Pairing/PairingTicketJson.cs)
- [PairingTicketCodeGenerator.cs](../../src/dotnet/Ansight.Core/Pairing/PairingTicketCodeGenerator.cs)
- [PairingDiscoveryHint.cs](../../src/dotnet/Ansight.Core/Pairing/Models/PairingDiscoveryHint.cs)

`ansight.pairing-ticket.v1` carries the signed `PairingConfig` plus the discovery metadata needed to reach the host. Legacy bootstrap and QR payload shapes are no longer accepted by the runtime-owned connection surface.

## Validation and trust

Before opening a session, the client validates:

- the pairing config signature
- the config expiry time
- the `appId`, when the caller supplies an expected app id

Signature verification uses the host public key embedded in the config and currently accepts several historical canonical JSON forms for compatibility. That behavior lives in [PairingConfigDocumentService.cs](../../src/dotnet/Ansight.Core/Pairing/PairingConfigDocumentService.cs) and [PairingCanonicalJson.cs](../../src/dotnet/Ansight.Core/Pairing/PairingCanonicalJson.cs).

Important current limitation: the UDP connect request and UDP connect response are not separately signed beyond normal JSON parsing and source-address checks.

## Host address resolution

The current `.NET` connector is ticket-driven.

- `ParsedPairingDocument.DiscoveryHint` is the primary source of the target host address
- `PairingConnectionOptions.HostAddressOverride` is an explicit escape hatch for advanced recovery scenarios
- there is no LAN discovery implementation in the current base `.NET` SDK beyond the hint already embedded in the ticket

## UDP connect handoff

The client sends a JSON packet with camel-cased property names using [PairingJson.cs](../../src/dotnet/Ansight.Core/Pairing/PairingJson.cs).

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

Model: [ConnectRequest.cs](../../src/dotnet/Ansight.Core/Pairing/Models/ConnectRequest.cs)

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
  "hostWifiName": "Office Wifi",
  "message": "ready",
  "webSocketPort": 45124,
  "webSocketPath": "/ws",
  "webSocketToken": "ws_token"
}
```

Model: [ConnectResponse.cs](../../src/dotnet/Ansight.Core/Pairing/Models/ConnectResponse.cs)

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
- after connect, the client immediately begins structured control requests

## WebSocket receive and control model

The transport implementation is in [PairingSessionTransport.cs](../../src/dotnet/Ansight.Core/Pairing/PairingSessionTransport.cs).

Current behavior:

- outgoing text and binary writes are serialized through a send lock
- request/response control sends use `SendControlRequestAsync(...)`
- every control request carries a unique request id
- the host replies with `CONTROL_RESP` envelopes correlated by `replyTo`
- WebSocket close frames are surfaced internally as the sentinel string `<close>`

The receive pump is text-oriented. Structured control traffic uses `CONTROL_REQ` and `CONTROL_RESP` JSON envelopes, while telemetry and screenshot streams remain fire-and-forget.

## Initial profile exchange

After the WebSocket is attached, [PairingSessionClient.cs](../../src/dotnet/Ansight.Core/Pairing/PairingSessionClient.cs) sends a baseline `DeviceAppProfile` if one is available.

Control action: `device.profile`

Model: [DeviceAppProfile.cs](../../src/dotnet/Ansight.Core/Pairing/Models/DeviceAppProfile.cs)

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

Important `device` fields include:

- `formFactor`: normalized form factor such as `phone`, `tablet`, `desktop`, `tv`, `watch`, `car`, or `vr`
- `isVirtual`: whether the app appears to be running on a virtual device, emulator, or simulator
- `isEmulator`: legacy-compatible emulator/simulator flag populated with the same value as `isVirtual` by the .NET SDK

This payload is sent with request/response semantics, so the client waits for a correlated control response after sending it.

## Telemetry message families

Telemetry streaming is implemented in [TelemetryStreamer.cs](../../src/dotnet/Ansight.Core/Telemetry/TelemetryStreamer.cs).

### `CLIENT_LOG`

Sent by [PairingSessionClient.cs](../../src/dotnet/Ansight.Core/Pairing/PairingSessionClient.cs) with request/ack semantics.

```json
{
  "source": "client",
  "type": "CLIENT_LOG",
  "sentAtUtc": "2026-03-20T10:00:00Z",
  "data": "log line"
}
```

### `CLIENT_DONE`

Sent by [PairingSessionClient.cs](../../src/dotnet/Ansight.Core/Pairing/PairingSessionClient.cs) with request/ack semantics. After the send completes, the client closes the session transport.

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

## Input capture message families

Input capture streaming is separate from metrics and telemetry events. Captured touches are not added to `IDataSink`, `Metric`, or `AppEvent`; they are emitted only on the live session socket while touch capture is enabled.

### `CLIENT_TOUCH_INPUT`

Sent fire-and-forget with `SendTextAsync(...)`. No acknowledgement is expected.

```json
{
  "source": "client",
  "type": "CLIENT_TOUCH_INPUT",
  "sentAtUtc": "2026-03-20T10:00:00Z",
  "touches": [
    {
      "id": "018f4d3e-9d79-7d91-a38b-0260f9e69a52",
      "action": "down",
      "capturedAtUtc": "2026-03-20T09:59:59Z",
      "pointerId": 0,
      "pointerIndex": 0,
      "pointerCount": 1,
      "x": 120.0,
      "y": 240.0,
      "normalizedX": 0.25,
      "normalizedY": 0.5,
      "surfaceWidth": 480.0,
      "surfaceHeight": 480.0,
      "coordinateSpace": "window",
      "coordinateUnit": "pixels",
      "surfaceScale": 2.0
    }
  ]
}
```

Current batching behavior:

- maximum `200` touch records per batch
- pending touch queue capped at `2000`
- Android coordinates are emitted in window pixels
- iOS and Mac Catalyst coordinates are emitted in window points

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

- [PairingToolProtocolProcessor.cs](../../src/dotnet/Ansight.Core/Pairing/PairingToolProtocolProcessor.cs)
- [ToolProtocolBridge.cs](../../src/dotnet/Ansight.Core/Tools/ToolProtocolBridge.cs)

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
- [PairingBinaryTransferHub.cs](../../src/dotnet/Ansight.Core/Pairing/PairingBinaryTransferHub.cs)
- [PairingFileTransferWireProtocol.cs](../../src/dotnet/Ansight.Core/Pairing/PairingFileTransferWireProtocol.cs)

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

- [PairingSessionJpegStreamer.cs](../../src/dotnet/Ansight.Core/Screenshot/PairingSessionJpegStreamer.cs)
- [SessionJpegWireProtocol.cs](../../src/dotnet/Ansight.Core/Screenshot/SessionJpegWireProtocol.cs)

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
- manual host IP selection from pairing documents, overrides, or remembered Wi-Fi profiles
- UDP `CONNECT_REQ` / `CONNECT_RESP` handoff
- WebSocket upgrade with an opaque host hello
- request/ack text messages for profile, logs, done, and event batches
- fire-and-forget text messages for metric channels and metric batches
- binary file transfer initiated by `files.begin_binary_download` using the `ASFT` frame header
- optional binary screenshot streaming using the `ASJP` frame header
- automatic `tool.query` / `tool.call` handling on the live session socket
