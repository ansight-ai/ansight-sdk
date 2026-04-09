# Ansight

Ansight captures in-process telemetry for .NET Android, iOS, and Mac Catalyst apps and includes the core pairing client used to open live sessions from a mobile app.

The base package supports direct/manual pairing and the Ansight UDP pairing handshake.

## Telemetry quickstart

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithFramesPerSecond()
    // JPEG session capture can affect runtime performance. Use conservative settings unless you need richer review snapshots.
    .WithSessionJpegCapture(intervalMilliseconds: 2000, quality: 60, maxWidth: 720)
    .Build();

Runtime.InitializeAndActivate(options);

Runtime.Metric(123, channel: 10);
Runtime.Event("network_request_started");
Runtime.ScreenViewed("CheckoutPage");
```

When `WithSessionJpegCapture(...)` is enabled, the pairing client will capture the app's own root window/view as a JPEG and stream it over live Ansight pairing sessions. Capture remains client-driven, but the next interval is delayed until the previous frame has finished encoding and sending so the stream self-throttles under load. Connected tooling can inspect the latest live frame or correlate historical frames with the telemetry timeline. This feature adds extra rendering, encoding, and transport work and can negatively affect runtime performance while it is active.

Host auto-probe is enabled by default. While `Runtime` is active, Ansight will periodically try to reconnect to the most recent successful host session if one is cached, pause probing while that session stays open, and resume after a reconnect delay if the session closes. Disable it with `WithoutHostAutoProbe()` or customize it with `WithHostAutoProbe(new HostAutoProbeOptions { ... })`.

Runtime-owned host connection now also owns saved and bundled config resolution. If your app bundles `ansight.developer-pairing.json`, Ansight now attempts startup auto-connect automatically when the runtime becomes active. Configure bundled config resolution once during runtime initialization, then use `Runtime.HostConnection` when you need to retry auto-connect explicitly, handle pairing configs, or recover from saved-config expiry.

```csharp
public static class AppBootstrap
{
    public static async Task ConfigureAnsightAsync(string payload)
    {
        var options = Options.CreateBuilder()
            .WithBundledHostConnection(typeof(AppBootstrap).Assembly)
            .Build();

        Runtime.InitializeAndActivate(options);
        var connectResult = await Runtime.HostConnection.ConnectAsync(
            HostConnectionRequest.PayloadText(payload, "pairing config"));
    }
}
```

When the pairing documents live in packaged text assets instead of embedded resources, use the bundled asset loader overload and keep the standard asset names:

```csharp
var options = Options.CreateBuilder()
    .WithBundledHostConnection(
        (assetName, cancellationToken) => TryLoadBundledTextAssetAsync(assetName, cancellationToken),
        configReader: new MyHostConnectionConfigReader())
    .Build();
```

## Data access

```csharp
var sink = Runtime.Instance.DataSink;
var allMetrics = sink.Metrics;
var allEvents = sink.Events;
```

## Pairing quickstart

Open a pairing session:

```csharp
using Ansight.Pairing;
using Ansight.Pairing.Models;

var client = new PairingSessionClient();

var result = await client.OpenSessionAsync(
    config,
    clientName: "My App",
    connectionOptions,
    progress: null,
    cancellationToken);
```

`OpenSessionAsync(...)` now sends a baseline `DeviceAppProfile` automatically immediately after the WebSocket handshake. Supply `PairingConnectionOptions.DeviceAppProfile` only when you want to add or override fields, or configure `UseDeviceAppProfileProvider(...)` on the builder to replace the automatic collector.

Apps that initialize the runtime should generally prefer `Runtime.HostConnection` over creating their own long-lived `PairingSessionClient` instances, because the runtime-owned surface coordinates stored configs, auto-probe, metrics streaming, and disconnect state in one place.

Create or parse a pairing config document:

```csharp
using Ansight.Pairing;
using Ansight.Pairing.Models;

var configDocument = new PairingConfigDocument
{
    Config = config,
    Discovery = discoveryHint
};

var payload = PairingConfigDocumentJson.Serialize(configDocument, indented: true);
var compactCode = PairingConfigCodeGenerator.Serialize(configDocument);

if (PairingConfigCodeGenerator.TryParse(compactCode, out var parsedConfigDocument))
{
    var resolvedConfigId = parsedConfigDocument!.Config.ConfigId;
}
```

## Remote tool registration

The base package owns the tool abstractions and registration surface, including per-tool argument/result schemas for bridges such as MCP, but concrete tool groups live in separate packages.

```csharp
using Ansight;
using Ansight.Tools.Database;
using Ansight.Tools.FileSystem;
using Ansight.Tools.Preferences;
using Ansight.Tools.Reflection;
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
    .WithReflectionTools(reflection =>
    {
        reflection.WithAssemblyTraversalMode(ReflectionAssemblyTraversalMode.AllowAll);
        reflection.WithNamespaceTraversalMode(ReflectionNamespaceTraversalMode.AllowAll);
        reflection.AddRoot(
            "session",
            new DebugSessionViewModel(),
            new ReflectionRootMetadata("Current Session"));
    })
    .WithSecureStorageTools(secure =>
    {
        secure.WithStorageIdentifier("MyApp");
        secure.AllowKey("session_token");
    })
    .WithReadWriteToolAccess()
    .Build();
```

The feature packages currently group tools by functional area:

Reflection roots support path-based write/invoke allow-lists plus type-wide helpers like `AllowAllWritableMembersOn<T>()` and `AllowAllInvokableMethodsOn<T>()` when an entire reachable type should be enabled.

- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Reflection`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`
- `Ansight.Tools.Preferences`
- `Ansight.Tools.SecureStorage`

Registered tools remain disabled until the app opts into a guard policy such as `WithReadOnlyToolAccess()`, `WithReadWriteToolAccess()`, or `WithAllToolAccess()`.
The storage packages mark `remove` operations as `Delete`, so those stay disabled unless the app chooses `WithAllToolAccess()` or a custom `ToolGuard`.
When a pairing session is open, inbound `tool.query` and `tool.call` protocol messages are handled automatically and answered on the active WebSocket using that guard policy.

For local temp-file workflows, `Ansight.Tools.FileSystem` exposes `files.begin_binary_download`, which returns transfer metadata and then streams `ASFT` binary frames over the pairing WebSocket. A bridge can map that `transferId` to its own temp directory and write the incoming bytes there.

## Embedded developer pairing target

The base package ships an optional MSBuild target that can prebundle a developer pairing config during build.

Enable it in your app project:

```xml
<PropertyGroup>
  <AnsightDeveloperPairingEnabled>true</AnsightDeveloperPairingEnabled>
</PropertyGroup>
```

Optional properties:

```xml
<PropertyGroup>
  <AnsightDeveloperPairingSourceFile>ansight.json</AnsightDeveloperPairingSourceFile>
  <AnsightDeveloperPairingOutputFile>$(BaseIntermediateOutputPath)ansight.developer-pairing.json</AnsightDeveloperPairingOutputFile>
</PropertyGroup>
```

Embed both pairing files as exact-name resources so the runtime can resolve them from `BundledConfigAssembly`:

```xml
<ItemGroup>
  <EmbeddedResource Include="ansight.json" LogicalName="ansight.json" />
  <EmbeddedResource Include="$(BaseIntermediateOutputPath)ansight.developer-pairing.json"
                    LogicalName="ansight.developer-pairing.json"
                    Condition="'$(AnsightDeveloperPairingEnabled)' == 'true' and Exists('$(BaseIntermediateOutputPath)ansight.developer-pairing.json')" />
</ItemGroup>
```

When enabled, the target reads your source pairing config, captures local machine metadata when available, and writes a pairing config document containing:

- the original `PairingConfig`
- a `PairingDiscoveryHint` with host addresses, machine name, and Wi-Fi name when available

On Unix it uses `generate-ansight-developer-pairing.sh`. On Windows it uses `generate-ansight-developer-pairing.ps1`.

## Build-time Remote Tool Enforcement

The base package enforces an explicit opt-in for bundled remote tools. By default, builds fail if the output contains concrete `Ansight.Tools.ITool` implementations.

To intentionally allow them, declare:

```xml
<PropertyGroup>
  <AnsightAllowRemoteTools>true</AnsightAllowRemoteTools>
</PropertyGroup>
```

If the property is omitted or set to `false`, Ansight scans the managed assemblies under `$(TargetDir)` after build and fails when it finds packaged tool assemblies such as `Ansight.Tools.VisualTree` or custom in-app `ITool` implementations.

Only use `AnsightAllowRemoteTools=true` for local Debug builds. Do not enable remote tools in Release or distributable builds, because they add remote inspection and action surfaces that can expose user data, screenshots, UI state, filesystem contents, database contents, and other privileged runtime capabilities to a connected client.

## Related packages

- `Ansight.Tools.VisualTree`: UI hierarchy and screenshot tools
- `Ansight.Tools.Reflection`: live object reflection and guarded runtime mutation tools
- `Ansight.Tools.Database`: database inspection tools
- `Ansight.Tools.FileSystem`: sandboxed file access tools
- `Ansight.Tools.Preferences`: shared-preferences and user-defaults tools
- `Ansight.Tools.SecureStorage`: encrypted storage and Keychain tools

## Notes

- Ansight is best-effort telemetry and has observer overhead.
- Use platform profilers for authoritative measurements.
- Pairing requires a config document with a current discovery hint or an explicit `HostAddressOverride`.
