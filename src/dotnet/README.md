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
    .Build();

Runtime.InitializeAndActivate(options);

Runtime.Metric(2048, channel: 10);
Runtime.Event("sync_started");
```

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

## Notes

- Ansight stores telemetry in-memory with a retention window.
- Sampling introduces observer overhead.
- Use platform profilers for authoritative measurements.
