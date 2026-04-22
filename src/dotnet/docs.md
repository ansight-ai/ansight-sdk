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

## Developer pairing and host connection

For local development, enable the base package's developer-pairing MSBuild target in Debug builds:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <AnsightDeveloperPairingEnabled>true</AnsightDeveloperPairingEnabled>
</PropertyGroup>
```

When enabled, the build writes and embeds `ansight.developer-pairing.json` automatically. If `ansight.json` exists in the project directory, the generated resource wraps that signed config with current local discovery metadata. If no source file exists, the generated resource is an `ansight.developer-pairing.v1` marker for Studio's development-only pairing path, so no checked-in pairing JSON is required.

Configure the runtime-owned host connection to read bundled resources from the app assembly:

```csharp
var options = Options.CreateBuilder()
    .WithBundledHostConnection(typeof(App).Assembly)
    .Build();
```

While `Runtime` is active, host auto-probe is enabled by default. It periodically tries the embedded developer pairing resource first, then falls back to cached sessions, saved configs, and plain bundled `ansight.json` configs. You can also request the same flow explicitly:

```csharp
await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.Auto());
```

Install `Ansight.Pairing` when the app should own native QR acquisition for explicit pairing overrides:

```csharp
using Ansight;
#if ANDROID
using Microsoft.Maui.ApplicationModel;
#endif

var optionsBuilder = Options.CreateBuilder()
    .WithBundledHostConnection(typeof(App).Assembly);

#if ANDROID
optionsBuilder = optionsBuilder.WithPlatformPairing(() => Platform.CurrentActivity);
#else
optionsBuilder = optionsBuilder.WithPlatformPairing();
#endif

Runtime.InitializeAndActivate(optionsBuilder.Build());

await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.QrCode());
```

Explicit requests such as `HostConnectionRequest.PayloadText(...)` and `HostConnectionRequest.QrCode(...)` replace the current host session even when developer pairing is configured.

For packaged text assets such as MAUI app assets, provide a shared loader for the standard asset names:

```csharp
var options = Options.CreateBuilder()
    .WithBundledHostConnection(
        (assetName, cancellationToken) => TryLoadBundledTextAssetAsync(assetName, cancellationToken),
        configReader: new MyHostConnectionConfigReader())
    .Build();
```

## Remote tool registration

The core `Ansight` package contains `ITool`, `ToolScope`, `ToolSchema`, `ToolDefinition`, `ToolRegistry`, `ToolResult`, and the `OptionsBuilder` registration methods. Each tool declares whether it is `Read`, `Write`, or `Delete`, plus explicit argument/result schemas for bridges such as MCP. A bridge can read `tool.Definition` or `options.Tools.GetDefinitions()` to discover how to call the tool. Concrete tool groups are delivered as separate packages and register through fluent extensions:

```csharp
using Ansight;
using Ansight.Tools.Database;
using Ansight.Tools.FileSystem;
using Ansight.Tools.Preferences;
using Ansight.Tools.Reflection;
using Ansight.Tools.SecureStorage;
using Ansight.Tools.VisualTree;

var session = new DebugSessionViewModel();

var sessionRoot = ReflectionRootRegistry.Register(
    "session",
    session,
    new ReflectionRootMetadata("Current Session")
    {
        Description = "Debug session view model",
        Hints = ["debug", "session"]
    },
    ReferenceType.Strong);

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithDatabaseTools()
    .WithFileSystemTools()
    .WithPreferencesTools(preferences =>
    {
        preferences.AllowKeyPrefix("com.example.");
    })
    .WithReflectionTools(reflection =>
    {
        reflection.WithDefaultMemberVisibility(ReflectionMemberVisibility.PublicOnly);
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

Reflection roots are the access boundary for `Ansight.Tools.Reflection`. Register a root with `ReflectionRootRegistry.Register(...)`, then the tools inspect reachable objects through stateless paths from that root. Direct object registrations are weak by default unless `ReferenceType.Strong` is passed; getter registrations use a `Func<object?>` when the exposed root can change over time and are unavailable while the getter returns `null`. The simplified options surface controls traversal and visibility only:

- `WithDefaultMemberVisibility(ReflectionMemberVisibility.PublicOnly)` keeps reflection to public members
- `WithDefaultMemberVisibility(ReflectionMemberVisibility.PublicAndNonPublic)` also exposes non-public members
- `AllowAssembly(...)`, `AllowAssemblies(...)`, `AllowNamespacePrefix(...)`, and `AllowNamespacePrefixes(...)` restrict expansion when paired with the `AllowListedOnly` traversal modes

Reflection read tools (`reflect.list_roots`, `reflect.inspect_object`, and `reflect.describe_type`) are available with `WithReadOnlyToolAccess()`. `reflect.set_member_value` and `reflect.invoke_method` are write-scoped and require `WithReadWriteToolAccess()` or a custom guard. Dispose the returned `ReflectionRootRegistrationHandle`, or call `ReflectionRootRegistry.Deregister(id)`, when a root should no longer be exposed.

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

For file inspection, `Ansight.Tools.FileSystem` exposes `files.get_file_checksum` for sandboxed file fingerprints across `md5`, `sha1`, `sha256`, `sha384`, `sha512`, and `crc32`.

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

Treat `AnsightAllowRemoteTools=true` as a local-Debug-only override. Do not enable remote tools in Release or distributable builds, because they add remote inspection and execution surfaces that can expose sensitive app data and privileged runtime behavior to a connected client.

## Supported target frameworks

- `net9.0-android`
- `net9.0-ios`
- `net9.0-maccatalyst`
