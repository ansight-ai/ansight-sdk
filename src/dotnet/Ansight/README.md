# Ansight

Ansight captures in-process telemetry for .NET Android, iOS, and Mac Catalyst apps and includes the core pairing client used to connect a mobile app to an Ansight host.

The base package uses manual pairing against a known host address and the Ansight UDP pairing handshake.

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
```

When `WithSessionJpegCapture(...)` is enabled, the pairing client will periodically capture the app's own root window/view as a JPEG and stream it over live Ansight pairing sessions. Studio can then show the latest live frame or scrub historical frames against the telemetry timeline. This feature adds extra rendering, encoding, and transport work and can negatively affect runtime performance while it is active.

## Data access

```csharp
var sink = Runtime.Instance.DataSink;
var allMetrics = sink.Metrics;
var allEvents = sink.Events;
```

## Pairing quickstart

Use a direct/manual host address:

```csharp
using Ansight.Pairing;

var client = new PairingSessionClient();

var result = await client.OpenSessionAsync(
    config,
    clientName: "My App",
    new PairingConnectionOptions
    {
        DiscoveryMode = PairingDiscoveryMode.BasicManual,
        ManualHostAddress = "192.168.1.10"
    },
    progress: null,
    cancellationToken);
```

`OpenSessionAsync(...)` now sends a baseline `DeviceAppProfile` automatically immediately after the WebSocket handshake. Supply `PairingConnectionOptions.DeviceAppProfile` only when you want to add or override fields, or configure `UseDeviceAppProfileProvider(...)` on the builder to replace the automatic collector.

Create or parse a QR/bootstrap payload:

```csharp
using Ansight.Pairing;

var payload = QrDiscoveryPayload.Serialize(config, discoveryHint, indented: true);

if (QrDiscoveryPayload.TryParse(payload, out var document))
{
    var parsedConfig = document!.PairingConfig;
}
```

## Remote tool registration

The base package owns the tool abstractions and registration surface, including per-tool argument/result schemas for bridges such as MCP, but concrete tool groups live in separate packages.

```csharp
using Ansight;
using Ansight.Tools.Database;
using Ansight.Tools.FileSystem;
using Ansight.Tools.VisualTree;

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithDatabaseTools()
    .WithFileSystemTools()
    .WithReadOnlyToolAccess()
    .Build();
```

The feature packages currently group tools by capability area:

- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`

Registered tools remain disabled until the app opts into a guard policy such as `WithReadOnlyToolAccess()` or `WithAllToolAccess()`.
When a pairing session is open, inbound `tool.query` and `tool.call` protocol messages are handled automatically and answered on the active WebSocket using that guard policy.

## Embedded developer pairing target

The base package ships an optional MSBuild target that can prebundle a developer pairing bootstrap file during build.

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

When enabled, the target reads your source pairing config, captures local machine metadata when available, and writes a bootstrap document containing:

- the original `PairingConfig`
- a `PairingDiscoveryHint` with host IP, host name, and Wi-Fi name when available

On Unix it uses `generate-ansight-developer-pairing.sh`. On Windows it uses `generate-ansight-developer-pairing.ps1`.

## Build-time MCP tool enforcement

The base package enforces an explicit opt-in for bundled MCP tools. By default, builds fail if the output contains concrete `Ansight.Tools.ITool` implementations.

To intentionally allow them, declare:

```xml
<PropertyGroup>
  <AnsightAllowMCPTools>true</AnsightAllowMCPTools>
</PropertyGroup>
```

If the property is omitted or set to `false`, Ansight scans the managed assemblies under `$(TargetDir)` after build and fails when it finds packaged tool assemblies such as `Ansight.Tools.VisualTree` or custom in-app `ITool` implementations.

Only use `AnsightAllowMCPTools=true` for local Debug builds. Do not enable MCP tools in Release or distributable builds, because they add remote inspection and action surfaces that can expose user data, screenshots, UI state, filesystem contents, database contents, and other privileged runtime capabilities to a connected client.

## Related packages

- `Ansight.Tools.VisualTree`: UI hierarchy and screenshot tools
- `Ansight.Tools.Database`: database inspection tools
- `Ansight.Tools.FileSystem`: sandboxed file access tools

## Notes

- Ansight is best-effort telemetry and has observer overhead.
- Use platform profilers for authoritative measurements.
- Pairing requires a host IP address supplied manually or via a saved discovery hint.
