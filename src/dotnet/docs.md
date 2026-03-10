# Ansight .NET SDK Guide

Ansight is a telemetry sampler for .NET Android, iOS, and Mac Catalyst apps.

## Initialize

```csharp
var options = Options.CreateBuilder()
    .WithSampleFrequencyMilliseconds(500)
    .WithRetentionPeriodSeconds(10 * 60)
    .WithFramesPerSecond()
    .Build();

Runtime.Initialize(options);
Runtime.Activate();
```

Or initialize and activate in one call:

```csharp
Runtime.InitializeAndActivate(options);
```

## Record telemetry

```csharp
Runtime.Metric(12345, channel: 42);
Runtime.Event("cache_hit");
Runtime.Event("cache_miss", AppEventType.Warning);
Runtime.Event("download", AppEventType.Info, channel: 42, details: "size=8mb");
```

## Custom channels

```csharp
var options = Options.CreateBuilder()
    .AddAdditionalChannel(new Channel(42, "Cache", Colors.Orange))
    .Build();
```

Reserved channel IDs are rejected by `Options.Build()`.

## Read sampled data

```csharp
var sink = Runtime.Instance.DataSink;

var allChannels = sink.Channels;
var allMetrics = sink.Metrics;
var allEvents = sink.Events;

var recentMetrics = sink.GetMetricsForChannelInRange(42, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
var recentEvents = sink.GetEventsForChannelInRange(42, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
```

## FPS sampling

FPS is disabled by default unless enabled via options:

```csharp
var options = Options.CreateBuilder().WithFramesPerSecond().Build();
```

Toggle at runtime:

```csharp
Runtime.EnableFramesPerSecond();
Runtime.DisableFramesPerSecond();
```

## Lifecycle

```csharp
Runtime.Activate();
Runtime.Deactivate();
Runtime.Clear();
```

## Supported target frameworks

- `net9.0-android`
- `net9.0-ios`
- `net9.0-maccatalyst`
