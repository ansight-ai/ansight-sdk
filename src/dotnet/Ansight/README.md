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
```

When `WithSessionJpegCapture(...)` is enabled, the pairing client will periodically capture the app's own root window/view as a JPEG and stream it over live Ansight pairing sessions. Connected tooling can inspect the latest live frame or correlate historical frames with the telemetry timeline. This feature adds extra rendering, encoding, and transport work and can negatively affect runtime performance while it is active.

Host auto-probe is enabled by default. While `Runtime` is active, Ansight will periodically try to reconnect to the most recent successful pairing profile if one is cached, pause probing while that session stays open, and resume after a reconnect delay if the session closes. Disable it with `WithoutHostAutoProbe()` or customize it with `WithHostAutoProbe(new HostAutoProbeOptions { ... })`.

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

Create or parse a QR/bootstrap payload:

```csharp
using Ansight.Pairing;
using Ansight.Pairing.Models;

var payload = QrDiscoveryPayload.Serialize(config, discoveryHint, indented: true);

if (QrDiscoveryPayload.TryParseConnectionPayload(payload, out var document))
{
    var connectionHint = document!.Connection;
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

For local temp-file workflows, `Ansight.Tools.FileSystem` exposes `files.begin_binary_download`, which returns transfer metadata and then streams `ASFT` binary frames over the pairing WebSocket. A bridge can map that `transferId` to its own temp directory and write the incoming bytes there.

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
- a `PairingDiscoveryHint` with network address, machine name, and Wi-Fi name when available

On Unix it uses `generate-ansight-developer-pairing.sh`. On Windows it uses `generate-ansight-developer-pairing.ps1`.

## Build-time Remote Tool Enforcement

The base package enforces an explicit opt-in for bundled remote tools. By default, builds fail if the output contains concrete `Ansight.Tools.ITool` implementations.

To intentionally allow them, declare:

```xml
<PropertyGroup>
  <AnsightAllowRemoteTools>true</AnsightAllowRemoteTools>
</PropertyGroup>
```

If the property is omitted or set to `false`, Ansight scans the managed assemblies under `$(TargetDir)` after build and fails when it finds packaged tool assemblies such as `Ansight.Tools.VisualTree` or custom in-app `ITool` implementations. The legacy `AnsightAllowMCPTools` alias is still accepted for compatibility.

Only use `AnsightAllowRemoteTools=true` for local Debug builds. Do not enable remote tools in Release or distributable builds, because they add remote inspection and action surfaces that can expose user data, screenshots, UI state, filesystem contents, database contents, and other privileged runtime capabilities to a connected client.

## Related packages

- `Ansight.Tools.VisualTree`: UI hierarchy and screenshot tools
- `Ansight.Tools.Database`: database inspection tools
- `Ansight.Tools.FileSystem`: sandboxed file access tools

## Notes

- Ansight is best-effort telemetry and has observer overhead.
- Use platform profilers for authoritative measurements.
- Pairing requires a reachable address supplied manually or via a saved discovery hint.
