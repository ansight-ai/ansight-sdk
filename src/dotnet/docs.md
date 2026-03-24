# Ansight .NET SDK Guide

Ansight is a telemetry sampler for .NET Android, iOS, and Mac Catalyst apps.

## Initialize

```csharp
using Ansight;

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
using Ansight;
using Ansight.Telemetry.Events;

Runtime.Metric(12345, channel: 42);
Runtime.Event("cache_hit");
Runtime.Event("cache_miss", AppEventType.Warning);
Runtime.Event("download", AppEventType.Info, channel: 42, details: "size=8mb");
```

## Custom channels

```csharp
using Ansight;
using Ansight.Telemetry.Channels;

var options = Options.CreateBuilder()
    .AddAdditionalChannel(new Channel(42, "Cache", Colors.Orange))
    .Build();
```

Reserved channel IDs are rejected by `Options.Build()`.

## Read sampled data

```csharp
using Ansight;

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

## Remote tool registration

The core `Ansight` package contains `ITool`, `ToolScope`, `ToolSchema`, `ToolDefinition`, `ToolRegistry`, `ToolResult`, and the `OptionsBuilder` registration methods. Each tool declares whether it is `Read`, `Write`, or `Delete`, plus explicit argument/result schemas for bridges such as MCP. A bridge can read `tool.Definition` or `options.Tools.GetDefinitions()` to discover how to call the tool. Concrete tool groups are delivered as separate packages and register through fluent extensions:

```csharp
using Ansight;
using Ansight.Tools.Database;
using Ansight.Tools.FileSystem;
using Ansight.Tools.Preferences;
using Ansight.Tools.SecureStorage;
using Ansight.Tools.VisualTree;

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithDatabaseTools()
    .WithFileSystemTools()
    .WithPreferencesTools(preferences =>
    {
        preferences.AllowKeyPrefix("com.example.");
    })
    .WithSecureStorageTools(secure =>
    {
        secure.WithStorageIdentifier("MyApp");
        secure.AllowKey("session_token");
    })
    .WithReadWriteToolAccess()
    .Build();
```

Registered tools are guarded explicitly. Use:

- `WithToolsDisabled()` to disable discovery and execution
- `WithReadOnlyToolAccess()` to enable read tools
- `WithReadWriteToolAccess()` to enable read and write tools
- `WithAllToolAccess()` to enable all registered scopes
- `WithToolGuard(...)` for a custom policy

The storage packages register `remove` operations as `Delete`, so `WithReadWriteToolAccess()` intentionally keeps those hidden and non-executable.

The runtime exposes a protocol bridge for transport layers:

```csharp
var response = await Runtime.ToolBridge.HandleAsync(new ToolProtocolEnvelope
{
    Type = ToolProtocolBridge.QueryType,
    Id = "req_1",
    SessionId = "sess_1"
});
```

When a `PairingSessionClient` WebSocket session is open, inbound `tool.query` and `tool.call` messages are processed automatically and answered on the same socket. Discovery and execution remain subject to the configured `ToolGuard`.

`PairingSessionClient.ProcessToolProtocolMessageAsync(...)` remains available for manual/raw message processing outside the live WebSocket flow.

For MCP-style file extraction, `Ansight.Tools.FileSystem` exposes `files.begin_binary_download` for bridge implementations that want a real local temp file. The tool returns `downloadId`, `transferId`, `fileName`, `fileExtension`, `mimeType`, and a stable `version` token, then streams `ASFT` binary frames over the pairing WebSocket so the bridge can write them into a caller-chosen temp directory. `files.download_file` remains available as a JSON/base64 fallback.

## Build-time Remote Tool Enforcement

Ansight enforces an explicit opt-in for bundled remote tools. Builds fail by default when the managed assemblies in `$(TargetDir)` contain concrete `Ansight.Tools.ITool` implementations.

Allow them explicitly with:

```xml
<PropertyGroup>
  <AnsightAllowRemoteTools>true</AnsightAllowRemoteTools>
</PropertyGroup>
```

Leave the property unset, or set it to `false`, to keep the default build failure.

Treat `AnsightAllowRemoteTools=true` as a local-Debug-only override. The legacy `AnsightAllowMCPTools` alias is still accepted for compatibility. Do not enable remote tools in Release or distributable builds, because they add remote inspection and execution surfaces that can expose sensitive app data and privileged runtime behavior to a connected client.

## Supported target frameworks

- `net9.0-android`
- `net9.0-ios`
- `net9.0-maccatalyst`
