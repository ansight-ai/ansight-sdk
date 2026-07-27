# Ansight.Core

`Ansight.Core` captures in-process telemetry for .NET 9, Android, iOS, and Mac
Catalyst apps and includes the core pairing client used to open live sessions
from an app process.

The runtime namespace remains `Ansight`. This package supports direct/manual pairing, the Ansight UDP pairing handshake, tool abstractions, and the build-time safety targets. Use the `Ansight` package for the all-in-one app setup, or `Ansight.Maui` for the MAUI all-in-one setup.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Telemetry quickstart

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithFramesPerSecond()
    // Battery level is opt-in and only emits on platforms that expose a battery API.
    .WithBatteryLevel()
    // JPEG session capture will reduce FPS while frames are captured, encoded, and sent.
    // Use conservative settings unless you need richer review snapshots.
    .WithSessionJpegCapture(intervalMilliseconds: 2000, quality: 60, maxWidth: 720)
    .Build();

Runtime.InitializeAndActivate(options);

Runtime.Metric(123, channel: 10);
Runtime.Event("network_request_started");
Runtime.ScreenViewed("CheckoutPage");
```

`Runtime.ScreenViewed(...)` is the manual screen-view API for core and non-MAUI integrations. The `Ansight.Maui` all-in-one package records default MAUI page views automatically from `Application.PageAppearing` when the app is configured through `builder.UseAnsight(...)`.

When `WithSessionJpegCapture(...)` is enabled, the pairing client will capture the app's own root window/view as a JPEG and stream it over live Ansight pairing sessions. Capture remains client-driven, but the next interval is delayed until the previous frame has finished encoding and sending so the stream self-throttles under load. Connected tooling can inspect the latest live frame or correlate historical frames with the telemetry timeline.

> **Important:** Screen capture will result in an FPS drop while frames are
> captured, encoded, and transported. Disable session JPEG capture for
> performance-focused runs unless visual evidence is required.

Host auto-probe is enabled by default. While `Runtime` is active, Ansight remembers previous host connections and retries those profiles so the app can reconnect after the host disappears and later reappears. Probing pauses while a session stays open and resumes after the retry delay if the session closes. Remembered profiles are keyed by the Wi-Fi network reported by the host, store the latest host/LAN address, host name, discovery metadata, and signed pairing config for that network, and expire after 14 days by default. Disable auto-probe with `WithoutHostAutoProbe()`, customize the probing loop with `WithHostAutoProbe(new HostAutoProbeOptions { ... })`, or change profile expiry with `WithHostConnectionProfileRetention(...)`.

Runtime-owned host connection now also owns remembered, saved, bundled, and developer pairing config resolution. If you enable `AnsightDeveloperPairingEnabled`, the build must be given a signed pairing JSON through `AnsightDeveloperPairingSourceFile`; the generated `ansight.developer-pairing.json` is embedded into the app assembly automatically and startup auto-connect can discover it. Auto-connect prefers that developer pairing config when available, then cycles remembered Wi-Fi connection profiles, saved configs, and bundled-config fallbacks.

Install `Ansight.Pairing` when you are staying on `Ansight.Core` but still want Ansight to own native QR pairing acquisition. The `Ansight` and `Ansight.Maui` all-in-one packages already include it where supported.

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

Remembered host connection profile retention can be configured independently of telemetry retention:

```csharp
var options = Options.CreateBuilder()
    .WithHostConnectionProfileRetention(TimeSpan.FromDays(30))
    .Build();
```

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

If the bundled config loader is backed by authenticated app sync instead of a packaged asset, call `Runtime.HostConnection.NotifyConfigChangedAsync()` after sync adds, updates, or removes the config. The runtime re-reads and validates the effective bundled config source, updates host connection status/capabilities, and raises `StatusChanged` even when a valid config changed while availability stayed true. Call `ConnectAsync(HostConnectionRequest.BundledConfig())` when the app should connect using the latest synced config.

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

The core package owns the tool abstractions and registration surface, including per-tool argument/result schemas for bridges such as MCP, but concrete tool groups live in separate packages.

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

Reflection roots are the access boundary for `Ansight.Tools.Reflection`. Register a root with `ReflectionRootRegistry.Register(...)`, then the reflection tools inspect reachable objects through stateless paths from that root. `reflect.list_roots` includes a `hostRuntime` descriptor so callers can identify CLR-hosted roots. Direct object registrations are weak by default unless `ReferenceType.Strong` is passed; getter registrations use a `Func<object?>` when the exposed root can change over time and are unavailable while the getter returns `null`. The simplified reflection options builder controls traversal and member visibility only. Use `WithReadOnlyToolAccess()` for list, inspect, and describe-type tools; use `WithReadWriteToolAccess()` or a custom guard when `reflect.set_member_value` or `reflect.invoke_method` should be available. Dispose the returned `ReflectionRootRegistrationHandle`, or call `ReflectionRootRegistry.Deregister(id)`, when a root should no longer be exposed.

Registered tools remain disabled until the app opts into a guard policy such as `WithReadOnlyToolAccess()`, `WithReadWriteToolAccess()`, or `WithAllToolAccess()`. Use `Options.OptionsBuilder.ContainsTool(string)` when a setup helper needs to avoid registering a suite that was already customized by another builder call.
The storage packages mark `remove` operations as `Delete`, and `files.delete_file` is also delete-scoped, so those stay disabled unless the app chooses `WithAllToolAccess()` or a custom `ToolGuard`.
When a pairing session is open, inbound `tool.query` and `tool.call` protocol messages are handled automatically and answered on the active WebSocket using that guard policy.

For local temp-file workflows, `Ansight.Tools.FileSystem` exposes `files.begin_binary_download`, which returns transfer metadata and then streams `ASFT` binary frames over the pairing WebSocket. A bridge can map that `transferId` to its own temp directory and write the incoming bytes there. The same package can push base64 or UTF-8 content into an approved sandbox folder with `files.push_file`, copy files with `files.copy_file`, move or rename files with `files.move_file`, and delete files with `files.delete_file`.

## App artifact providers

App artifact providers expose dynamically available exports such as reports,
logs, traces, images, or state snapshots:

```csharp
using Ansight.Artifacts;

var options = Options.CreateBuilder()
    .AddArtifactProvider(new CurrentReportArtifactProvider())
    .WithReadOnlyToolAccess()
    .Build();
```

Implement `IArtifactProvider.QueryAsync(...)` to advertise
`ArtifactDefinition` values and `CreateAsync(...)` to return an
`ArtifactResult`. `ArtifactPayload` can read from text, bytes, a stream factory,
or an app-local file.

The first configured provider automatically registers `artifacts.query` and
`artifacts.request`. Both tools are read-scoped, but their providers can export
sensitive app data, so definitions should include accurate `ToolSecurity`
metadata and remain protected by an appropriate `ToolGuard`. Requested bytes
use the live pairing binary-transfer channel and are unavailable without an
active Studio tool request.

Separate packages can also initialize an SDK extension with
`OptionsBuilder.AddRuntimeFeature(...)`. An `IRuntimeFeature` has a stable id
and receives the newly created `IRuntime` once. Initialization failures are
logged and isolated from core runtime startup. `Ansight.Annotations` uses this
extension point for its opt-in feedback service.

## Embedded developer pairing target

The core package ships an optional MSBuild target that can embed a signed local-development pairing config during build.

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

When enabled, the target writes `$(AnsightDeveloperPairingOutputFile)` and embeds it into the consuming assembly automatically as `ansight.developer-pairing.json`. No extra `EmbeddedResource` item is required for the generated developer pairing file.

`AnsightDeveloperPairingSourceFile` is required and must point at a signed pairing JSON. The build fails when developer pairing is enabled and the source file is missing.

If you also want a plain bundled fallback config, keep embedding `ansight.json` manually and configure `WithBundledHostConnection(...)`:

```xml
<ItemGroup>
  <EmbeddedResource Include="ansight.json" LogicalName="ansight.json" />
</ItemGroup>
```

With the generated developer resource available, or with `WithBundledHostConnection(typeof(AppBootstrap).Assembly)` configured for explicit bundled config loading:

- auto-connect prefers the embedded developer pairing config when available, then cycles valid remembered Wi-Fi connection profiles newest-first, then falls back to saved configs and any plain bundled config
- explicit `HostConnectionRequest.PayloadText(...)` and `HostConnectionRequest.QrCode(...)` requests always use the supplied pairing payload and replace the current host session

The target reads the source pairing config, captures local machine metadata when available, and writes a pairing config document containing:

- the original `PairingConfig`
- a `PairingDiscoveryHint` with host addresses, machine name, and Wi-Fi name when available

On Unix it uses `generate-ansight-developer-pairing.sh`. On Windows it uses `generate-ansight-developer-pairing.ps1`.

Security and integration considerations:

- the generated embedded resource includes host addresses, host name, Wi-Fi name, and a capture timestamp from the build machine
- saved, pasted, QR, compact-code, bundled, and developer pairing config flows require signed pairing configs and keep signature, expiry, token, and app-id validation
- keep `AnsightDeveloperPairingEnabled=true` limited to local development and Debug builds
- do not ship developer pairing resources in CI, Release, TestFlight, App Store, Play Store, or other distributable builds

## Build-time Remote Tool Enforcement

The core package can scan build outputs for bundled and custom remote tools. The scanner examines the managed assemblies under `$(TargetDir)` for concrete `Ansight.Tools.ITool` implementations.

Control build-time remote tool handling with `AnsightRemoteToolsPolicy`:

- `Allowed`: bypasses remote tool scanning, warnings, and detected-tool logging.
- `AllowedWithWarnings`: scans for remote tools, logs detected tool type and assembly details, emits a build warning when tools are present, and allows the build to continue. This is the default.
- `Disallowed`: scans for remote tools, logs detected tool type and assembly details, and fails the build when tools are present.

When the resolved policy is `Allowed` or `AllowedWithWarnings`, Ansight sets `AnsightRemoteToolsEnabled=true` and adds the `ANSIGHT_REMOTE_TOOLS` compile-time symbol. `Disallowed` sets `AnsightRemoteToolsEnabled=false` and omits that symbol.

For strict Release or CI builds, declare:

```xml
<PropertyGroup>
  <AnsightRemoteToolsPolicy>Disallowed</AnsightRemoteToolsPolicy>
</PropertyGroup>
```

`Disallowed` will not work with the `Ansight` or `Ansight.Maui` all-in-one packages as-is because those packages intentionally include remote tools. To exercise this policy, use `Ansight.Core` plus the fine-grained `Ansight.Tools.*` packages and condition the tool references out of protected Release or CI builds.

Detected tool logging is enabled by default. To suppress the type and assembly list while keeping the selected policy behavior, set `AnsightLogRemoteTools=false`.

Use `Allowed` only when the build intentionally includes remote tools and you do not want build-time checks or warnings. Do not enable remote tools in Release or distributable builds unless the app has an explicit user-authorization model, because they add remote inspection and action surfaces that can expose user data, screenshots, UI state, filesystem contents, database contents, and other privileged runtime capabilities to a connected client.

## Related packages

- `Ansight`: all-in-one package for non-MAUI apps
- `Ansight.Maui`: all-in-one package for MAUI apps
- `Ansight.Core`: core runtime package
- `Ansight.Annotations`: opt-in Debug-only annotated feedback
- `Ansight.OfflineCapture`: retained offline sessions, export, and team upload
- `Ansight.Pairing`: native QR pairing acquisition for runtime-owned host connections
- `Ansight.Tools.Maui`: MAUI inspection and mutation tools
- `Ansight.Tools.VisualTree`: UI hierarchy and screenshot tools
- `Ansight.Tools.Reflection`: live object reflection and guarded runtime mutation tools
- `Ansight.Tools.Database`: database inspection tools
- `Ansight.Tools.FileSystem`: sandboxed file access tools
- `Ansight.Tools.Preferences`: shared-preferences and user-defaults tools
- `Ansight.Tools.SecureStorage`: encrypted storage and Keychain tools

## Notes

- Ansight is best-effort telemetry and has observer overhead.
- Use platform profilers for authoritative measurements.
- Pairing requires a config document with a current discovery hint, an explicit `HostAddressOverride`, or a known simulator/emulator where the SDK can fall back to the host machine address.
