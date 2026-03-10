# Ansight — telemetry sampler for .NET mobile apps

Ansight captures runtime telemetry for .NET Android, iOS, and Mac Catalyst apps.

It samples memory and optional FPS, stores recent samples in-memory, and exposes them through `IDataSink`.

## Quickstart

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithFramesPerSecond()
    .Build();

Runtime.InitializeAndActivate(options);

Runtime.Metric(123, channel: 10);
Runtime.Event("network_request_started");
```

## Data access

```csharp
var sink = Runtime.Instance.DataSink;
var allMetrics = sink.Metrics;
var allEvents = sink.Events;
```

## Notes

- Ansight is best-effort telemetry and has observer overhead.
- Use platform profilers for authoritative measurements.
