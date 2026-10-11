# Ansight.Connectivity

Opt-in passive connection state, transport changes, and optional OS link quality for Ansight. Included in the all-in-one `Ansight` package.

```csharp
using Ansight;
using Ansight.Connectivity;

Runtime.InitializeAndActivate(Options.CreateBuilder()
    .WithConnectivityCapture(options => options.MinimumSampleIntervalMilliseconds = 5_000)
    .Build());
```

Use the same options builder as your offline capture setup. Apple and Android use native OS observers. Portable .NET desktop provides availability only. Unavailable measurements remain unknown; no active network probes or network identifiers are collected.

Captured data can be inspected in the local/cloud Connectivity tab and with `ansight session connectivity <session-id> summary --json`.

See the [connectivity guide](https://github.com/ansight-ai/ansight-sdk/blob/main/docs/connectivity.md) for platform support and capture format.
