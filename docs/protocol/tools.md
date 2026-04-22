# Ansight Remote Tool Protocol

This document describes the remote-tool message flow currently implemented by the `.NET` SDK on top of the pairing WebSocket session.

The implementation lives in:

- [PairingToolProtocolProcessor.cs](../../src/dotnet/Ansight/Pairing/PairingToolProtocolProcessor.cs)
- [ToolProtocolBridge.cs](../../src/dotnet/Ansight/Tools/ToolProtocolBridge.cs)
- [ToolProtocolEnvelope.cs](../../src/dotnet/Ansight/Tools/ToolProtocolEnvelope.cs)
- [ToolGuard.cs](../../src/dotnet/Ansight/Tools/ToolGuard.cs)

## Preconditions

Automatic tool handling on the session socket works when:

- a pairing WebSocket session is open
- the runtime is initialized
- the configured `ToolGuard` allows discovery and, for calls, execution

If the runtime is not initialized, the SDK replies with `tool.error`.

Catalog contents and executable tool ids depend on which tools were registered in `Options`. If no tools are registered, `tool.query` returns an empty catalog and `tool.call` returns `tool_not_found`.

## Envelope

All tool protocol messages use the `ToolProtocolEnvelope` shape:

```json
{
  "type": "tool.query",
  "id": "req_1",
  "replyTo": null,
  "sessionId": "sess_1",
  "sentAt": "2026-03-20T10:00:00Z",
  "capability": "tool.exec",
  "payload": {}
}
```

Fields:

- `type`: message kind
- `id`: required request or response id
- `replyTo`: optional parent request id
- `sessionId`: optional logical session id
- `sentAt`: UTC timestamp
- `capability`: defaults to `tool.exec`
- `payload`: message body

Current capability constant: `tool.exec`

## Transport behavior

During normal session receive processing:

- inbound `tool.query` and `tool.call` messages are intercepted automatically
- the SDK processes them and writes a response on the same WebSocket
- intercepted tool messages are not forwarded into the normal acknowledgement queue
- messages with other `type` values are ignored by the tool processor and continue through the normal session path

If a message has `type = tool.query` or `type = tool.call` but cannot be parsed, the SDK replies with `tool.error` using code `tool_protocol_invalid_request`.

If a message includes `capability` and the value is not `tool.exec`, the current transport does not treat it as a tool protocol message.

## Supported request types

### `tool.query`

Requests the visible tool catalog.

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

Success response type: `tool.catalog`

Response payload shape:

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

If discovery is disabled by the current guard, the SDK replies with `tool.error` and code `tool_discovery_disabled`.

### `tool.call`

Requests tool execution.

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
- `payload.toolId` is required
- `payload.arguments` is optional
- argument values are flattened to string values before the registered tool executes

Success response type: `tool.result`

Response payload shape:

```json
{
  "toolId": "example.tool",
  "success": true,
  "message": "ok",
  "result": {}
}
```

Tool results remain ordinary JSON payloads. A tool can either keep the entire transfer inside `tool.result` payloads, or it can return JSON metadata first and then use an out-of-band binary stream on the same WebSocket. The `.NET` file-system package supports JSON metadata, JSON file transfer, and binary file transfer paths.

`files.get_file_checksum` returns sandboxed file metadata plus a `checksums` array. The `algorithms` argument accepts `md5`, `sha1`, `sha256`, `sha384`, `sha512`, `crc32`, or `all`, and defaults to `sha256`.

`files.download_file` keeps the transfer in JSON and returns:

- file metadata such as `fileName`, `fileExtension`, and `mimeType`
- a stable `version` token for resumable reads
- `offsetBytes`, `bytesRead`, `hasMore`, and `nextOffsetBytes`
- either `text` (`encoding = utf-8`) or `base64` (`encoding = base64`)
- a `nextRequest` object containing the next `tool.call` payload when more data remains

`files.begin_binary_download` returns:

- `downloadId` and `transferId`
- `fileName`, `fileExtension`, `mimeType`, `sizeBytes`, and `version`
- `deliveryMode = websocket_binary`
- `wireProtocol = ansight.file-transfer.v1`

After that `tool.result` is sent, the SDK starts emitting binary WebSocket frames for the requested file. That path is intended for MCP bridges that want to write directly into a caller-chosen temp directory and then return the local path to the agent.

The host-side reference implementation for that flow lives in `Ansight.Host` as `BinaryFileDownloadManager`.

## Response types

### `tool.catalog`

Returned for successful `tool.query` requests.

Envelope behavior:

- `id = <requestId>.response`
- `replyTo = <requestId>`
- `capability = tool.exec`

### `tool.result`

Returned for successful `tool.call` requests.

Envelope behavior:

- `id = <requestId>.response`
- `replyTo = <requestId>`
- `capability = tool.exec`

### `tool.error`

Returned when the request is invalid, discovery is disabled, execution is denied, the tool is missing, or the tool throws/fails.

Error payload shape:

```json
{
  "code": "tool_not_found",
  "message": "Tool 'example.tool' is not registered.",
  "retryable": false,
  "details": null
}
```

Common current error codes:

- `tool_protocol_invalid_request`
- `tool_runtime_not_initialized`
- `tool_discovery_disabled`
- `tool_call_payload_invalid`
- `tool_call_missing_id`
- `tool_not_found`
- `tool_execution_denied`
- `tool_execution_failed`
- `tool_execution_exception`

## Tool catalog contents

Catalog entries are generated from registered `ToolDefinition` values and currently include:

- `id`
- `name`
- `description`
- `category`
- `scope`
- `keywords`
- `security` when the tool supplies a structured security annotation
- `argumentsSchema`
- `resultSchema`

The `security` object is informational metadata intended for catalog consumers such as Studio or MCP bridges. In the current `.NET` implementation it contains:

- `level`
- `summary`
- `implications`

`argumentsSchema` and `resultSchema` are emitted from [ToolSchema.cs](../../src/dotnet/Ansight/Tools/ToolSchema.cs) as JSON-schema-like objects.

The actual tool ids available on a given session are implementation-defined and depend on:

- which tool packages the app registered
- the current `ToolGuard`

## Current limitations

The current `.NET` implementation does not add:

- cancellation messages
- progress events
- generic transport-level chunked tool results
- binary artifact references inside the tool protocol itself
- request correlation beyond normal `id` / `replyTo`
- a separate capability namespace per tool family

Individual tools may still page their own results with ordinary JSON fields such as offsets or continuation arguments. `files.download_file` uses that pattern for resumable file transfer.

The tool protocol still does not embed host-local file paths or host temp-directory decisions. Those remain the responsibility of the MCP bridge consuming the SDK's `files.begin_binary_download` result and subsequent binary frames.

Large screenshots are not transported through `tool.result`; they use the session's separate binary screenshot stream described in [README.md](README.md).
