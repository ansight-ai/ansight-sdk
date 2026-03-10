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

## Notes

- Ansight stores telemetry in-memory with a retention window.
- Sampling introduces observer overhead.
- Use platform profilers for authoritative measurements.
