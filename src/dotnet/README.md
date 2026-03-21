# Ansight — telemetry sampling for .NET mobile apps

[![Ansight](https://img.shields.io/nuget/vpre/Ansight.svg?cacheSeconds=3600&label=Ansight%20nuget)](https://www.nuget.org/packages/Ansight)

Ansight provides lightweight in-process telemetry sampling for:

- .NET Android
- .NET iOS
- .NET Mac Catalyst

## What it captures

- Managed heap usage
- Platform memory usage (RSS/native heap/physical footprint, per platform)
- Optional FPS samples
- Custom metrics and events via channels

## Quickstart

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithFramesPerSecond()
    // JPEG session capture can affect runtime performance. Use conservative settings unless you need richer review snapshots.
    .WithSessionJpegCapture(intervalMilliseconds: 2000, quality: 60, maxWidth: 720)
    .Build();

Runtime.InitializeAndActivate(options);

Runtime.Metric(2048, channel: 10);
Runtime.Event("sync_started");
```

When `WithSessionJpegCapture(...)` is enabled, the pairing client will periodically capture the app's own root window/view as a JPEG and stream it over live Ansight pairing sessions. Studio can then show the latest live frame or scrub historical frames against the telemetry timeline. This feature adds extra rendering, encoding, and transport work and can negatively affect runtime performance while it is active.

## Accessing sampled data

```csharp
var sink = Runtime.Instance.DataSink;
var metrics = sink.Metrics;
var events = sink.Events;
```

## Remote tools

Tool abstractions stay in the `Ansight` package. Each tool exposes a `ToolDefinition` with argument/result schemas so a bridge can discover how to call it. Concrete tool groups are installed separately and attached through the options builder.

```csharp
using Ansight;
using Ansight.Tools.VisualTree;

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithReadOnlyToolAccess()
    .Build();
```

Available grouped packages:

- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`

At runtime, transport layers can query or execute tools through `Runtime.ToolBridge`. When a `PairingSessionClient` session is open, inbound `tool.query` and `tool.call` envelopes are handled automatically on the live WebSocket and answered according to the configured `ToolGuard`.

Pairing sessions also send a baseline `DeviceAppProfile` automatically after the WebSocket handshake so hosts can always capture app/device details without per-app setup.

`Ansight.Tools.FileSystem` includes `files.begin_binary_download` for bridge-oriented sandbox file transfer. The tool reports `transferId`, `fileExtension`, `mimeType`, and a stable `version` token, then streams `ASFT` binary frames over the pairing WebSocket so an MCP bridge can write the file into a caller-chosen temp directory and return that local path to the agent. `files.download_file` remains as a JSON/base64 fallback.

## Build-time MCP tool enforcement

`Ansight` fails builds by default when the built output contains concrete `Ansight.Tools.ITool` implementations.

To explicitly allow MCP tools in an app build, set:

```xml
<PropertyGroup>
  <AnsightAllowMCPTools>true</AnsightAllowMCPTools>
</PropertyGroup>
```

If `AnsightAllowMCPTools` is omitted or `false`, the SDK scans the managed assemblies under `$(TargetDir)` after build and errors on bundled tool implementations.

Keep `AnsightAllowMCPTools=true` limited to local Debug builds. MCP tools should never be enabled in Release or shippable builds because they expose remote inspection and privileged action capabilities over app data and runtime state.

## Notes

- Ansight stores telemetry in-memory with a retention window.
- Sampling introduces observer overhead.
- Use platform profilers for authoritative measurements.
