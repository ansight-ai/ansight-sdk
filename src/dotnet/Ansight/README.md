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

Runtime-owned host connection now also owns saved, bundled, and developer pairing config resolution. If you enable `AnsightDeveloperPairingEnabled`, the generated `ansight.developer-pairing.json` is embedded into the app assembly automatically and startup auto-connect can discover it without a checked-in pairing JSON. Auto-connect prefers that developer pairing when available, then falls back to cached-session, saved-config, and other bundled-config behavior.

Install `Ansight.Pairing` when you want Ansight to own native QR pairing acquisition.

```csharp
public static class AppBootstrap
{
    public static async Task ConfigureAnsightAsync()
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
        var connectResult = await Runtime.HostConnection.ConnectAsync(HostConnectionRequest.Auto());
    }
}
```

On Android, `Ansight.Pairing` only needs the current `Activity` so it can launch the scanner UI for `HostConnectionRequest.QrCode(...)`.

When the pairing documents live in packaged text assets instead of embedded resources, use the bundled asset loader overload and keep the standard asset names:

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

Explicit requests such as `HostConnectionRequest.PayloadText(...)` and `HostConnectionRequest.QrCode(...)` always use the supplied pairing payload and replace the current host session. That gives QR/paste flows an explicit override path even when developer pairing is configured by default.

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

The feature packages currently group tools by functional area:

- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Reflection`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`
- `Ansight.Tools.Preferences`
- `Ansight.Tools.SecureStorage`

Reflection roots are the access boundary for `Ansight.Tools.Reflection`. Register a root with `ReflectionRootRegistry.Register(...)`, then the reflection tools inspect reachable objects through stateless paths from that root. The simplified reflection options builder controls traversal and member visibility only. Use `WithReadOnlyToolAccess()` for list, inspect, and describe-type tools; use `WithReadWriteToolAccess()` or a custom guard when `reflect.set_member_value` or `reflect.invoke_method` should be available. Dispose the returned `ReflectionRootRegistrationHandle`, or call `ReflectionRootRegistry.Deregister(id)`, when a root should no longer be exposed.

Registered tools remain disabled until the app opts into a guard policy such as `WithReadOnlyToolAccess()`, `WithReadWriteToolAccess()`, or `WithAllToolAccess()`.
The storage packages mark `remove` operations as `Delete`, so those stay disabled unless the app chooses `WithAllToolAccess()` or a custom `ToolGuard`.
When a pairing session is open, inbound `tool.query` and `tool.call` protocol messages are handled automatically and answered on the active WebSocket using that guard policy.

For local temp-file workflows, `Ansight.Tools.FileSystem` exposes `files.begin_binary_download`, which returns transfer metadata and then streams `ASFT` binary frames over the pairing WebSocket. A bridge can map that `transferId` to its own temp directory and write the incoming bytes there.

## Embedded developer pairing target

The base package ships an optional MSBuild target that can embed a local-development pairing marker during build.

Enable it in your app project:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
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

When enabled, the target writes `$(AnsightDeveloperPairingOutputFile)` and embeds it into the consuming assembly automatically as `ansight.developer-pairing.json`. No pairing JSON is required for development pairing, and no extra `EmbeddedResource` item is required for the generated developer pairing file.

If you also want a plain bundled fallback config, keep embedding `ansight.json` manually and configure `WithBundledHostConnection(...)`:

```xml
<ItemGroup>
  <EmbeddedResource Include="ansight.json" LogicalName="ansight.json" />
</ItemGroup>
```

With the generated developer resource available, or with `WithBundledHostConnection(typeof(AppBootstrap).Assembly)` configured for explicit bundled config loading:

- auto-connect prefers the embedded developer pairing when available, then falls back to cached sessions, saved configs, and any plain bundled config
- explicit `HostConnectionRequest.PayloadText(...)` and `HostConnectionRequest.QrCode(...)` requests always use the supplied pairing payload and replace the current host session

When enabled and the source file is absent, the target captures local machine metadata when available and writes an `ansight.developer-pairing.v1` marker. That marker tells the SDK to use the development-only connection path, and Studio accepts the request without a pre-issued signed pairing config.

When `AnsightDeveloperPairingSourceFile` exists, the target keeps the existing signed config behavior: it reads the source pairing config, captures local machine metadata when available, and writes a pairing config document containing:

- the original `PairingConfig`
- a `PairingDiscoveryHint` with host addresses, machine name, and Wi-Fi name when available

On Unix it uses `generate-ansight-developer-pairing.sh`. On Windows it uses `generate-ansight-developer-pairing.ps1`.

Security and integration considerations:

- the generated embedded resource includes host addresses, host name, Wi-Fi name, and a capture timestamp from the build machine
- normal saved, pasted, QR, compact-code, and bundled pairing config flows still require signed pairing configs and keep signature, expiry, token, and app-id validation
- keep `AnsightDeveloperPairingEnabled=true` limited to local development and Debug builds
- do not ship developer pairing resources in CI, Release, TestFlight, App Store, Play Store, or other distributable builds

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

- `Ansight.Pairing`: native QR pairing acquisition for runtime-owned host connections
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
