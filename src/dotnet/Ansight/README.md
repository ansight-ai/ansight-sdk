# Ansight

Ansight captures in-process telemetry for .NET Android, iOS, and Mac Catalyst apps and includes the core pairing client used to connect a mobile app to an Ansight host.

The base package does not depend on any automatic discovery transport. Discovery is opt-in through an injected pairing strategy.

## Telemetry quickstart

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

Use an injected automatic discovery strategy:

```csharp
using Ansight.Discovery.Multicast;
using Ansight.Pairing;

var client = PairingSessionClient.CreateBuilder()
    .UseHostDiscoveryStrategy(MulticastPairingHostDiscoveryStrategy.Instance)
    .Build();

var result = await client.OpenSessionAsync(
    config,
    clientName: "My App",
    progress: null,
    cancellationToken);
```

Create or parse a QR/bootstrap payload:

```csharp
using Ansight.Pairing;

var payload = QrDiscoveryPayload.Serialize(config, discoveryHint, indented: true);

if (QrDiscoveryPayload.TryParse(payload, out var document))
{
    var parsedConfig = document!.PairingConfig;
}
```

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

## Related packages

- `Ansight.Discovery.Multicast`: UDP multicast host discovery strategy for automatic LAN pairing

## Notes

- Ansight is best-effort telemetry and has observer overhead.
- Use platform profilers for authoritative measurements.
- Automatic discovery is intentionally not built into the base package. Consumers opt in by providing an `IPairingHostDiscoveryStrategy`.
