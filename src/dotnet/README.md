# Ansight — telemetry sampling for .NET mobile apps

[![Ansight](https://img.shields.io/nuget/vpre/Ansight.svg?cacheSeconds=3600&label=Ansight%20nuget)](https://www.nuget.org/packages/Ansight)

Ansight provides lightweight in-process telemetry sampling for:

- .NET Android
- .NET iOS
- .NET Mac Catalyst

## What it captures

- Managed heap usage
- Platform memory usage (RSS/native heap/physical footprint, per platform)
- Optional FPS samples
- Custom metrics and events via channels

## Quickstart

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithFramesPerSecond()
    // JPEG session capture can affect runtime performance. Use conservative settings unless you need richer review snapshots.
    .WithSessionJpegCapture(intervalMilliseconds: 2000, quality: 60, maxWidth: 720)
    .Build();

Runtime.InitializeAndActivate(options);

Runtime.Metric(2048, channel: 10);
Runtime.Event("sync_started");
Runtime.ScreenViewed("HomePage");
```

When `WithSessionJpegCapture(...)` is enabled, the pairing client will capture the app's own root window/view as a JPEG and stream it over live Ansight pairing sessions. Capture remains client-driven, but the next interval is delayed until the previous frame has finished encoding and sending so the stream self-throttles under load. Connected tooling can inspect the latest live frame or correlate historical frames with the telemetry timeline. This feature adds extra rendering, encoding, and transport work and can negatively affect runtime performance while it is active.

Host auto-probe is enabled by default. While `Runtime` is active, Ansight will periodically try to reconnect to the most recent successful host session if one is cached, pause probing while that session stays open, and resume after a reconnect delay if the session closes. Disable it with `WithoutHostAutoProbe()` or customize it with `WithHostAutoProbe(new HostAutoProbeOptions { ... })`.

Runtime-owned host connection also manages saved and bundled pairing configs. If you enable `AnsightDeveloperPairingEnabled` and initialize with `WithBundledHostConnection(typeof(AppBootstrap).Assembly)`, the generated `ansight.developer-pairing.json` is embedded into the app assembly automatically. Auto-connect prefers that developer pairing when available, then falls back to cached-session, saved-config, and other bundled-config behavior.

Install `Ansight.Pairing` when you want the SDK to own native QR acquisition for explicit pairing overrides.

```csharp
public static class AppBootstrap
{
    public static void ConfigureAnsight()
    {
        var optionsBuilder = Options.CreateBuilder()
            .WithBundledHostConnection(typeof(AppBootstrap).Assembly);

#if ANDROID
        optionsBuilder = optionsBuilder.WithPlatformPairing(() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
#else
        optionsBuilder = optionsBuilder.WithPlatformPairing();
#endif

        var options = optionsBuilder.Build();

        Runtime.InitializeAndActivate(options);
    }
}
```

On Android, `Ansight.Pairing` only needs the current `Activity` so it can launch the scanner UI for `HostConnectionRequest.QrCode(...)`.

Explicit requests such as `HostConnectionRequest.PayloadText(...)` and `HostConnectionRequest.QrCode(...)` always use the supplied pairing payload and replace the current host session. That gives QR/paste flows an explicit override path even when developer pairing is configured by default.

For app-package assets such as MAUI `MauiAsset`s, use the bundled config loader overload:

```csharp
var optionsBuilder = Options.CreateBuilder()
    .WithBundledHostConnection(
        (assetName, cancellationToken) => TryLoadBundledTextAssetAsync(assetName, cancellationToken));

#if ANDROID
optionsBuilder = optionsBuilder.WithPlatformPairing(() => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity);
#else
optionsBuilder = optionsBuilder.WithPlatformPairing();
#endif

var options = optionsBuilder.Build();
```

## Accessing sampled data

```csharp
var sink = Runtime.Instance.DataSink;
var metrics = sink.Metrics;
var events = sink.Events;
```

## Remote tools

Tool abstractions stay in the `Ansight` package. Each tool exposes a `ToolDefinition` with argument/result schemas so a bridge can discover how to call it. Concrete tool groups are installed separately and attached through the options builder.

```csharp
using Ansight;
using Ansight.Tools.VisualTree;
using Ansight.Tools.Reflection;

ReflectionRootRegistry.Register(
    "session",
    new DebugSessionViewModel(),
    new ReflectionRootMetadata("Current Session")
    {
        Hints = ["debug", "session"]
    });

var options = Options.CreateBuilder()
    .WithVisualTreeTools()
    .WithReflectionTools()
    .WithReadOnlyToolAccess()
    .Build();
```

Available grouped packages:

- `Ansight.Pairing`
- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Reflection`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`
- `Ansight.Tools.Preferences`
- `Ansight.Tools.SecureStorage`

`WithReadWriteToolAccess()` enables read and write tools while keeping delete-scoped tools disabled. The storage packages register remove operations as `Delete`, so use `WithAllToolAccess()` or a custom `ToolGuard` when you want key removal enabled.

The reflection suite uses `ReflectionRootRegistry` as the singleton access boundary. Once a root is registered, reachable visible members can be inspected, writable members can be updated, and instance methods can be invoked. Use `ReflectionRootRegistry.Register(...)` and `Deregister(...)` at any point in the app lifecycle. Registrations are weak by default; pass `ReferenceType.Strong` when the registry should retain the root.

At runtime, transport layers can query or execute tools through `Runtime.ToolBridge`. When a `PairingSessionClient` session is open, inbound `tool.query` and `tool.call` envelopes are handled automatically on the live WebSocket and answered according to the configured `ToolGuard`.

Pairing sessions also send a baseline `DeviceAppProfile` automatically after the WebSocket handshake so connected tooling can capture app/device details without per-app setup.

`Ansight.Tools.FileSystem` includes `files.begin_binary_download` for bridge-oriented sandbox file transfer. The tool reports `transferId`, `fileExtension`, `mimeType`, and a stable `version` token, then streams `ASFT` binary frames over the pairing WebSocket so a bridge can materialize the file in a caller-chosen local temp directory and return that path to the caller. `files.download_file` remains as a JSON/base64 fallback.

## Build-time Remote Tool Enforcement

`Ansight` fails builds by default when the built output contains concrete `Ansight.Tools.ITool` implementations.

To explicitly allow remote tools in an app build, set:

```xml
<PropertyGroup>
  <AnsightAllowRemoteTools>true</AnsightAllowRemoteTools>
</PropertyGroup>
```

If `AnsightAllowRemoteTools` is omitted or `false`, the SDK scans the managed assemblies under `$(TargetDir)` after build and errors on bundled tool implementations.

Keep `AnsightAllowRemoteTools=true` limited to local Debug builds. Remote tools should never be enabled in Release or shippable builds because they expose remote inspection and privileged action capabilities over app data and runtime state.

## Notes

- Ansight stores telemetry in-memory with a retention window.
- Sampling introduces observer overhead.
- Use platform profilers for authoritative measurements.
