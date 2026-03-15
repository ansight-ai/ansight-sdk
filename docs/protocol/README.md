# Ansight Connection Protocol

This document describes the transport and session design used between an Ansight-enabled app, Studio, and any MCP-compatible host/service.

It covers:

- how peers discover each other
- how pairing and trust are established
- how the session upgrades to a real-time channel
- what features the protocol currently supports
- where remote tools fit into the design

## Design goals

- Keep pairing lightweight enough for local developer workflows.
- Separate discovery from trust so transport mechanisms can change without rewriting the security model.
- Use a single upgraded session channel for telemetry, control, and future tool execution.
- Allow Studio and MCP to share the same capabilities model, even if they present different UX.

## Roles

- `app client`: the SDK running inside the mobile or desktop app
- `host`: the developer machine or service accepting the session
- `studio`: the primary interactive UI for inspection and debugging
- `mcp adapter`: a machine-oriented facade that exposes the same remote capabilities as tools/resources/prompts

In the current codebase, the app acts as the initiating client and the host acts as the accepting server.

## Transport overview

The protocol currently uses a three-stage connection model:

1. `Discovery`
   The app finds the host either by manual address entry or LAN discovery.
2. `UDP connect handoff`
   The app sends a signed pairing-derived connect request to the host discovery port.
3. `WebSocket session`
   The host accepts the request and upgrades the session to a WebSocket channel used for telemetry and control messages.

## Default ports and endpoints

Current defaults in the SDK:

- discovery UDP port: `45123`
- WebSocket port: `45124`
- WebSocket path: `/ws`
- mDNS service name: `_ansightstream._tcp`

These values come from [PairingProtocolDefaults.cs](../../src/dotnet/Ansight/Pairing/PairingProtocolDefaults.cs).

## Pairing artifacts

The trust model is driven by a signed pairing document.

### Pairing config

The base pairing object is `PairingConfig` and contains:

- protocol/schema identity
- `configId`
- target `appId` and `appName`
- issue and expiry timestamps
- a one-time token
- host identity and public key material
- challenge settings
- trust policy flags
- host signature

Relevant fields are defined in [PairingModels.cs](../../src/dotnet/Ansight/Pairing/PairingModels.cs).

### Bootstrap document

The SDK also supports a wrapper document:

- schema: `ansight.pairing-bootstrap.v1`
- payload: `pairingConfig`
- optional: `discovery`

The optional `discovery` object is a `PairingDiscoveryHint` that can include:

- host IP/address
- host name
- Wi-Fi name
- capture timestamp
- source metadata

This is useful for QR handoff, local developer bootstrap files, or pre-bundled configuration.

## Discovery layer

Two discovery modes exist today:

- `BasicManual`
  The app connects to an explicitly supplied host IP address.
- `ConfiguredStrategy` / `AutomaticMulticast`
  The app uses an injected discovery strategy, currently multicast/LAN discovery for .NET.

### Multicast discovery

The optional multicast package uses UDP multicast LAN discovery.

Request shape:

- `type`: `DISCOVER_REQ`-style request identifier
- `ver`: protocol version
- `nonce`
- `appId`

Response shape:

- `type`
- `ver`
- `hostId`
- `hostName`
- `wsPort`
- `wsPath`
- `hostPubKey`
- `respNonce`
- `sig`

The important property of discovery is that it is not trusted by itself. The client validates the response signature against the public key already embedded in the pairing config before using the host.

## Session establishment

After discovery, the app sends a UDP connect request to the host.

### Connect request

Current request fields:

- `type = CONNECT_REQ`
- `ver = 1`
- `configId`
- `oneTimeToken`
- `appId`
- `clientName`

### Connect response

Current response fields:

- `type = CONNECT_RESP`
- `ver = 1`
- `accepted`
- `reason`
- `reasonMessage`
- `hostId`
- `hostName`
- `message`
- `webSocketPort`
- `webSocketPath`
- `webSocketToken`

If accepted, the host returns a WebSocket handoff token and endpoint details.

## WebSocket session

The real-time session is established as:

```text
ws://<host-address>:<webSocketPort><webSocketPath>?token=<webSocketToken>
```

The client opens the socket, waits for an initial host hello message, and then begins sending telemetry and control payloads.

### Current message families

Implemented client-to-host message types in the .NET SDK:

- `DeviceAppProfile`
- `CLIENT_LOG`
- `CLIENT_DONE`
- `CLIENT_METRIC_CHANNELS`
- `CLIENT_METRICS`
- `CLIENT_EVENTS`

Some payloads expect a host acknowledgement, while high-frequency telemetry payloads such as metric channel definitions and metric batches are currently sent without waiting for an ack.

### Device/app profile

This is the initial capability and environment snapshot the app can send after the socket opens. The schema is `ansight.device-app-profile.v1`.

It can include:

- device identity and OS details
- battery, display, GPU, thermal, and network metadata
- app version/build/debuggable flags
- runtime stack metadata
- graphics/runtime settings
- permissions and tags

This profile should be treated as the session's initial context block and can be refreshed later if needed.

## Supported features

Features currently reflected in the codebase:

- signed pairing configuration validation
- optional bootstrap/QR documents
- manual host connection
- optional multicast LAN discovery
- UDP connect handshake
- WebSocket session handoff
- device/app profile exchange
- client log forwarding
- real-time metric streaming
- event streaming
- explicit session completion and close

Features implied but not fully implemented yet:

- host-to-client command messages
- negotiated tool execution
- richer capability advertisement
- resumable/recoverable sessions
- binary payload transport for screenshots or large visual trees

## Protocol structure going forward

The current protocol is good enough for telemetry streaming, but remote inspection features need one additional layer: explicit capability negotiation.

Recommended session structure:

1. `host_hello`
   Includes protocol version, host identity, and supported capabilities.
2. `client_hello`
   Includes SDK version, platform/runtime identity, and requested capability set.
3. `session_ready`
   Freezes the negotiated feature set for the session.
4. `event streams`
   Telemetry, logs, and lifecycle events.
5. `command streams`
   Tool requests/responses, subscriptions, and long-running task progress.

### Capability negotiation

Studio and MCP should not assume every target can execute every command. Capabilities should be explicit and versioned.

For the baseline read-only inspection surface, "capabilities" should mean features the SDK can infer and expose automatically from the running app session. They should not require the app team to add extra OS permissions, entitlements, or custom capability declarations just to unlock standard inspection tools.

Recommended capability groups:

- `telemetry.metrics`
- `telemetry.events`
- `telemetry.logs`
- `inspect.device_profile`
- `inspect.visual_tree`
- `inspect.screenshot`
- `inspect.navigation`
- `storage.sql.read`
- `storage.sql.write`
- `fs.read`
- `fs.write`
- `fs.list`
- `automation.intent`
- `tool.exec`

Each capability should advertise:

- stable id
- semantic version or revision
- read/write/risky classification
- payload size limits
- whether streaming responses are supported

## Communications design guidance

### Envelope

The protocol will be easier to evolve if all WebSocket messages converge on a common envelope:

```json
{
  "type": "tool.call",
  "id": "req_123",
  "sessionId": "sess_abc",
  "sentAt": "2026-03-15T10:00:00Z",
  "capability": "inspect.screenshot",
  "payload": {}
}
```

Recommended shared fields:

- `type`: message kind
- `id`: request/event correlation id
- `replyTo`: optional parent correlation id
- `sessionId`: logical session identifier
- `sentAt`: UTC timestamp
- `seq`: optional ordered stream sequence
- `capability`: feature/tool namespace
- `payload`: typed body

### Delivery semantics

- Telemetry can remain best-effort and lossy under backpressure.
- Tool execution should be request/response with explicit success, error, and cancellation states.
- Large inspections such as screenshots or deep trees should support chunking or artifact URLs instead of forcing one huge JSON message.
- Long-running actions should emit progress events.

### Error model

All command-like operations should return structured failures:

- `code`: stable machine-readable code
- `message`: user-readable explanation
- `retryable`: whether the caller should retry
- `details`: optional structured context

### Security model

Current trust anchors:

- signed pairing config
- host public key validation
- one-time token on first connection
- optional discovery restrictions via trust policy

Recommended additions for remote tooling:

- explicit per-tool risk classification
- host-side allow/deny policy by capability
- optional user-presence confirmation for risky commands
- scoped app-sandbox file system and SQL access
- audit log of executed tool requests

## Compatibility and versioning

Recommended versioning rules:

- keep transport versioning coarse at the envelope/protocol level
- version schemas independently where needed
- require tolerant readers for additive fields
- only break message shape on a major protocol version

For now, the codebase uses integer `ver` fields in UDP discovery/connect messages plus schema names for higher-level JSON payloads. That is a sensible short-term approach and can coexist with a more formal envelope version later.

## Relationship to MCP

MCP should be treated as a presentation layer over the same core capabilities, not a separate execution model.

That means:

- Studio can speak the native Ansight protocol directly for low-latency streaming.
- An MCP server can translate the same capabilities into MCP tools/resources.
- Capability ids, argument schemas, and result schemas should be shared so both surfaces stay aligned.

The proposed tool catalog lives in [tools.md](tools.md).

## Current gaps

Areas that still need implementation or formalization:

- host hello/session-ready schema
- standard WebSocket envelope
- tool request/response messages
- binary artifact transport
- subscription model for continuously changing inspection data
- capability/risk negotiation
- host-driven commands

## Summary

The current Ansight protocol already has a clear backbone:

- signed pairing config for trust
- UDP discovery/connect for fast local handoff
- WebSocket for live session traffic

The next step is to formalize capabilities and command execution so Studio and MCP can both drive the same remote inspection surface without splitting the protocol.
