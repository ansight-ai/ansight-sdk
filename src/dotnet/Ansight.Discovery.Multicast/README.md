# Ansight.Discovery.Multicast

`Ansight.Discovery.Multicast` provides UDP multicast LAN discovery for Ansight pairing.

This package is optional. The base `Ansight` package does not reference it directly.

## What it contains

- `MulticastPairingHostDiscoveryStrategy`: an `IPairingHostDiscoveryStrategy` implementation for automatic host discovery
- `MulticastDiscoveryClient`: low-level multicast discovery client helpers
- `MulticastDiscoveryServer`: low-level multicast server primitives for host-side discovery and UDP connect handoff

## Client usage

Inject the multicast strategy into `PairingSessionClient`:

```csharp
using Ansight.Discovery.Multicast;
using Ansight.Pairing;

var client = PairingSessionClient.CreateBuilder()
    .UseHostDiscoveryStrategy(MulticastPairingHostDiscoveryStrategy.Instance)
    .Build();
```

Then open a session without setting `BasicManual` mode:

```csharp
var result = await client.OpenSessionAsync(
    config,
    clientName: "My App",
    progress: null,
    cancellationToken);
```

## Host usage

Hosts can use `MulticastDiscoveryServer` to:

- respond to multicast discovery probes
- validate and answer UDP connect requests
- advertise WebSocket handoff details

## Behavior

- Discovery only succeeds when the pairing config allows LAN discovery.
- The strategy validates the discovery response signature against the pairing config host public key before returning a host IP.
- The package is transport-specific. Manual pairing and QR payload handling remain in other packages.
