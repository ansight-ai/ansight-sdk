# Ansight Remote Tools Proposal

This document proposes the remote tools that Ansight Studio and any MCP adapter should expose over the same capability model.

The aim is not to mirror every internal platform API. The aim is to provide a small, stable, high-value surface for debugging, inspection, and controlled remediation.

The baseline tool set should work without requiring additional OS permissions, entitlements, or app-specific capability declarations beyond integrating the Ansight SDK itself.

## Design principles

- One canonical tool catalog shared by Studio and MCP.
- Stable ids and argument/result schemas.
- Clear read-only versus mutating versus risky operations.
- Prefer structured data over raw text whenever possible.
- Make large outputs streamable or paged.
- Keep platform-specific fields inside result payloads, not in tool ids.
- Make the default read-only tool set available with zero additional app setup.

## Scope constraints

The initial tool surface should assume:

- no extra runtime permission prompts
- no new platform entitlements
- no accessibility-service style privileges
- no host-machine file system access from inside the app
- no custom app wiring just to expose baseline read-only inspection data

The tools should operate on data the app can already access in-process.

## Tool categories

- UI inspection
- diagnostics and telemetry
- app storage and data
- sandboxed file access
- app control
- device context

## Execution model

Each tool should support:

- `id`
- `title`
- `description`
- `risk`
- `argumentsSchema`
- `resultSchema`
- `supportsStreaming`
- `supportsCancellation`

Recommended risk levels:

- `read`
- `write`
- `destructive`

The default Studio and MCP experience should focus on `read` tools that are immediately available after pairing.

## Proposed tools

### UI inspection

#### `ui.get_visual_tree`

Returns the current UI hierarchy for the foreground window/page/view.

Use cases:

- inspect layout structure
- identify hidden/overlapping elements
- map automation targets
- debug navigation state

Suggested arguments:

- `includeBounds: bool`
- `includeComputedStyles: bool`
- `maxDepth: int`
- `rootNodeId: string?`

Suggested result:

- tree of nodes with ids, types, labels, bounds, visibility, focusability, children

Notes:

- Should be read-only.
- Should support partial subtree fetches for large trees.
- Must be derived from the app's own UI/runtime state, not from privileged platform inspection APIs.

#### `ui.get_screenshot`

Captures a screenshot of the current app scene.

Suggested arguments:

- `format: "png" | "jpeg"`
- `quality: int?`
- `maxWidth: int?`
- `annotateNodeIds: bool`

Suggested result:

- artifact reference or base64 payload
- width/height
- capture timestamp

Notes:

- Prefer artifact/chunk transport over inlining large images in JSON.
- This is one of the highest-value tools for Studio and MCP.
- Capture should be limited to the app's own rendered surface.

#### `ui.inspect_node`

Returns detailed information for a specific visual tree node.

Suggested arguments:

- `nodeId: string`
- `includeAncestors: bool`
- `includeDescendants: bool`
- `includeProperties: bool`

Suggested result:

- node metadata
- accessibility data
- layout data
- styling/property bag
- action affordances

#### `ui.perform_action`

Requests a safe interaction against a node.

Suggested arguments:

- `nodeId: string`
- `action: "tap" | "long_press" | "focus" | "scroll_into_view"`
- `parameters: object?`

Suggested result:

- success/failure
- action receipt
- optional updated node state

Notes:

- This is mutating and should require explicit capability approval.
- This is optional and should not be part of the baseline no-extra-setup tool set.

#### `ui.get_navigation_state`

Returns the current navigation stack and visible route/screen state.

Suggested arguments:

- `includeHistory: bool`

Suggested result:

- active route
- route stack
- modal stack
- tabs/selected segment
- platform-specific container details

### Diagnostics and telemetry

#### `diag.get_device_profile`

Returns the current device/app/runtime profile, aligned with `ansight.device-app-profile.v1`.

Suggested arguments:

- `refresh: bool`

Suggested result:

- same structure as the device profile payload already used during session setup

#### `diag.get_logs`

Fetches recent log lines or structured log entries.

Suggested arguments:

- `since: datetime?`
- `level: string?`
- `limit: int`
- `contains: string?`

Suggested result:

- list of log entries with timestamp, level, source, message, metadata

Notes:

- This should read logs already emitted or captured from inside the app/session.

#### `diag.subscribe_metrics`

Creates a live metrics subscription.

Suggested arguments:

- `channels: string[] | int[]`
- `sampleWindowMs: int?`

Suggested result:

- subscription id
- initial channel definitions
- stream of metric batches

Notes:

- Prefer subscription semantics rather than repeated polling.

#### `diag.get_events`

Returns recent app events from the in-process event buffer.

Suggested arguments:

- `since: datetime?`
- `channel: int?`
- `eventType: string?`
- `limit: int`

Suggested result:

- list of events with ids, labels, channel, type, details, timestamps

### App storage and data

#### `data.list_datastores`

Lists known application data stores.

Suggested arguments:

- `includeSystemStores: bool`

Suggested result:

- SQLite databases
- preferences stores
- cache locations
- document directories

Notes:

- Results should be limited to app-accessible sandbox locations.

#### `data.execute_sql`

Executes SQL against an explicitly named SQLite database that the app can already open from its own sandbox.

Suggested arguments:

- `database: string`
- `statement: string`
- `parameters: object?`
- `mode: "read" | "write"`

Suggested result:

- rows and columns for reads
- affected row count for writes
- execution timing
- truncated flag if row cap reached

Notes:

- Reads should be in the baseline tool set.
- Writes should be optional.
- Database access must stay within app-accessible sandbox files.

#### `data.get_preferences`

Reads key/value preference data.

Suggested arguments:

- `namespace: string?`
- `keys: string[]?`

Suggested result:

- structured map of values and type hints

#### `data.set_preferences`

Writes preference values.

Suggested arguments:

- `namespace: string?`
- `values: object`

Suggested result:

- applied keys
- rejected keys

### Sandboxed file access

#### `fs.list`

Lists files and directories inside the app's own sandbox roots.

Suggested arguments:

- `path: string`
- `recursive: bool`
- `includeHidden: bool`
- `limit: int`

Suggested result:

- entries with name, path, type, size, timestamps

Notes:

- This is app-sandbox listing, not host-machine filesystem listing.
- Good default roots are documents, cache, temp, and known app data directories.

#### `fs.read_text`

Reads a UTF-8 text file.

Suggested arguments:

- `path: string`
- `offset: int?`
- `length: int?`

Suggested result:

- text content
- encoding
- truncated flag

Notes:

- Limit reads to app-owned files.

#### `fs.read_binary`

Reads a binary file as an artifact or byte stream.

Suggested arguments:

- `path: string`

Suggested result:

- artifact handle
- mime type
- size

Notes:

- Limit reads to app-owned files.

#### `fs.write_text`

Writes or replaces a text file.

Suggested arguments:

- `path: string`
- `content: string`
- `createDirectories: bool`
- `overwrite: bool`

Suggested result:

- bytes written
- resulting path

Notes:

- This should remain optional, not baseline.

#### `fs.delete`

Deletes a file or directory within an approved scope.

Suggested arguments:

- `path: string`
- `recursive: bool`

Suggested result:

- deleted item count

Notes:

- This should be classified as destructive.
- This should remain optional, not baseline.

### App control

#### `app.get_state`

Returns app lifecycle and session state.

Suggested arguments:

- none

Suggested result:

- foreground/background
- active page/screen
- session uptime
- build/configuration flags

#### `app.trigger_gc`

Requests a GC cycle where supported.

Suggested arguments:

- `mode: "default" | "aggressive"`

Suggested result:

- before/after memory snapshot

Notes:

- Platform support will vary.
- This should not require extra permission, but it remains optional.

#### `app.simulate_memory_warning`

Triggers a low-memory simulation hook where the platform permits it.

Suggested arguments:

- none

Suggested result:

- whether the platform accepted the request

Notes:

- This is optional and platform-dependent.

#### `app.navigate`

Requests app navigation to a known route or screen.

Suggested arguments:

- `route: string`
- `parameters: object?`

Suggested result:

- success/failure
- resulting navigation state

Notes:

- This is valuable in Studio, but it should remain optional rather than baseline.

### Device context

#### `device.get_network_state`

Returns current connectivity details.

Suggested arguments:

- none

Suggested result:

- transport
- metered flag
- effective type
- RTT/downlink estimates when available

#### `device.get_display_info`

Returns display metrics and orientation information.

Suggested arguments:

- none

Suggested result:

- bounds
- density
- refresh rate
- orientation

## MVP recommendation

If this needs to ship incrementally, the first tool set should be:

- `ui.get_visual_tree`
- `ui.get_screenshot`
- `ui.inspect_node`
- `ui.get_navigation_state`
- `diag.get_device_profile`
- `diag.get_logs`
- `diag.get_events`
- `data.execute_sql`
- `fs.list`
- `fs.read_text`

That set gives Studio and MCP enough surface area to answer most debugging questions without requiring new permissions or high-risk mutation flows.

## Not in scope for the baseline tool set

These are intentionally excluded from the default tool surface:

- host-machine filesystem browsing from the app session
- device-wide filesystem browsing outside the app sandbox
- privileged inspection of other apps
- OS-level screenshot capture outside the app's own surface
- privileged database access to stores the app cannot already open
- baseline tools that require app teams to add custom handlers just to expose basic read-only inspection

## Studio and MCP mapping

Recommended mapping:

- native protocol capability id: `ui.get_visual_tree`
- Studio action: "Inspect visual tree"
- MCP tool name: `ui_get_visual_tree`

The underlying argument and result schemas should stay identical. Only the presentation layer should differ.

## Guardrails

The following guardrails should exist before tool execution is enabled broadly:

- sensible defaults that do not require app-specific capability declarations for the baseline read-only tool set
- explicit risk classification
- approved sandbox roots for file access
- approved database list for SQL access
- row, file size, and payload limits
- timeouts and cancellation
- audit trail for mutating operations
- optional user confirmation for risky commands

## Result transport guidance

Recommended response patterns:

- small JSON result: inline in a normal response
- large structured result: paged or chunked response
- binary result: artifact handle plus metadata
- long-running work: started/progress/completed event sequence

## Future additions

Likely follow-on tools once the basics exist:

- network request history inspection
- HTTP replay against app-configured clients
- keychain/secure storage inspection with explicit consent
- notification inbox inspection
- performance trace capture windows
- view snapshot diffing
- accessibility audit

## Summary

The highest-value initial tools are visual tree inspection, screenshot capture, device profile, logs/events, SQL reads, and constrained app-sandbox file access. Those capabilities are broad enough to power both Studio workflows and MCP-driven agent workflows without requiring new permissions or a fragmented remote control surface.
