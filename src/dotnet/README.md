# Ansight .NET SDK

Ansight provides in-process telemetry, host pairing, live JPEG capture, and guarded remote tools for .NET Android, iOS, and Mac Catalyst apps.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Package Model

- `Ansight.Core`: core runtime, telemetry, host pairing protocol, tool abstractions, and build-time safety targets.
- `Ansight`: all-in-one package for non-MAUI .NET apps. It depends on `Ansight.Core`, native pairing where supported, and all non-MAUI remote tool packages.
- `Ansight.Maui`: all-in-one package for .NET MAUI apps. It depends on `Ansight` and adds MAUI inspection/mutation tools plus `MauiAppBuilder` setup helpers with automatic lifecycle and page-view telemetry.
- `Ansight.Tools.*`: individual tool packages for apps that want explicit package-by-package control.

The runtime namespace remains `Ansight` even when the NuGet package is `Ansight.Core`.

## All-In-One

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithBundledHostConnection(typeof(AppBootstrap).Assembly);
#if ANDROID
        ansight.WithPlatformPairing(() => CurrentActivityProvider());
#endif
    })
    .Build();

Runtime.InitializeAndActivate(options);
```

`WithAnsightSdk(...)` applies the same practical defaults that Redpoint has been using:

- FPS sampling enabled
- 400ms sample frequency
- 120s retention
- live JPEG capture every 2000ms at quality 60 and max width 480
- host auto-probe enabled
- bundled host connection configured from the entry assembly, or overridden through `WithBundledHostConnection(...)`
- all non-MAUI remote tools registered
- full tool access enabled

> **Important:** Screen capture will result in an FPS drop while frames are
> captured, encoded, and transported. Disable session JPEG capture for
> performance-focused runs unless visual evidence is required.

The callback runs after runtime defaults and before default tool-suite registration. Use it to grant access for suites that are deny-all by default:

```csharp
using Ansight;
using Ansight.Tools.SecureStorage;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithSecureStorageTools(secure =>
        {
            secure.WithStorageIdentifier("ExampleApp");
            secure.AllowKeyPrefix("ansight.secure.");
        });
    })
    .Build();
```

When a callback registers a suite with `WithSecureStorageTools(...)`, `WithPreferencesTools(...)`, or another tool builder, the all-in-one skips its default registration for that suite. The all-in-one applies full tool access before the callback, so the callback can also override the guard with `WithReadOnlyToolAccess()`, `WithReadWriteToolAccess()`, or `WithToolGuard(...)`.

Use `WithAnsightDefaults()` when you want the runtime defaults without remote tools, or `WithAnsightRemoteTools()` when you only want the non-MAUI tool registrations.

## MAUI

```csharp
using Ansight.Maui;

public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();

    builder
        .UseMauiApp<App>()
        .UseAnsight<App>();

    return builder.Build();
}
```

`UseAnsight<App>()` initializes and activates the runtime from the MAUI builder. It also records foreground/background lifecycle transitions on Android, iOS, and Mac Catalyst, and records a screen-view event from `Application.PageAppearing` whenever a MAUI page appears.

You do not need to call `Runtime.SetAppLifecycleState(...)` from platform delegates or `Runtime.ScreenViewed(...)` from each page for the default MAUI telemetry.

For custom tool options:

```csharp
using Ansight.Maui;
using Ansight.Tools.Preferences;
using Ansight.Tools.SecureStorage;

builder.UseAnsight<App>(ansight =>
{
    ansight.WithPreferencesTools(preferences =>
    {
        preferences.AllowKeyPrefix("com.example.");
    });
    ansight.WithSecureStorageTools(secure =>
    {
        secure.WithStorageIdentifier("ExampleApp");
        secure.AllowKey("session_token");
    });
});
```

`UseAnsight(...)` and `WithAnsightMaui(...)` callbacks receive the existing `Options.OptionsBuilder` before the default tool suites are registered, so custom configuration uses the same core model as the lower-level SDK. When manually building options with `WithAnsightMaui(...)`, still pass them to `builder.UseAnsight(options)` so the MAUI automatic telemetry hooks are registered.

## Core Runtime

For apps that only want telemetry and pairing infrastructure:

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithFramesPerSecond()
    .WithBatteryLevel()
    .WithSessionJpegCapture(intervalMilliseconds: 2000, quality: 60, maxWidth: 720)
    .WithBundledHostConnection(typeof(AppBootstrap).Assembly)
    .Build();

Runtime.InitializeAndActivate(options);
```

Battery level telemetry is disabled by default; `WithBatteryLevel()` only emits on platforms that expose a battery API.

Install `Ansight.Core` for this lower-level surface. Add `Ansight.Pairing` separately if the app should own native QR acquisition while staying on the core package set.

### Telemetry and sampled data

```csharp
using Ansight;
using Ansight.Telemetry.Channels;
using Ansight.Telemetry.Events;

var options = Options.CreateBuilder()
    .AddAdditionalChannel(new Channel(42, "Cache", Colors.Orange))
    .Build();

Runtime.InitializeAndActivate(options);

Runtime.Metric(12345, channel: 42);
Runtime.Event("cache_hit");
Runtime.Event("cache_miss", AppEventType.Warning);
Runtime.Event("download", AppEventType.Info, channel: 42, details: "size=8mb");
```

Reserved channel IDs are rejected by `Options.Build()`. Recent samples can be read from the active runtime data sink:

```csharp
var sink = Runtime.Instance.DataSink;

var allChannels = sink.Channels;
var allMetrics = sink.Metrics;
var allEvents = sink.Events;

var recentMetrics = sink.GetMetricsForChannelInRange(
    42,
    DateTime.UtcNow.AddMinutes(-1),
    DateTime.UtcNow);
```

FPS sampling can be toggled at runtime after it has been configured:

```csharp
Runtime.EnableFramesPerSecond();
Runtime.DisableFramesPerSecond();
```

### Custom session properties

Apps can register grouped scalar custom properties that are sent on `session.open` and updated while a live pairing session is connected:

```csharp
var options = Options.CreateBuilder()
    .RegisterCustomProperty("app", "tenant", "acme")
    .RegisterCustomProperty("flags", "beta", true)
    .Build();

Runtime.InitializeAndActivate(options);
Runtime.RegisterCustomProperty("app", "region", "au");
```

Calling `RegisterCustomProperty(group, key, value)` again replaces the existing value. Use `RemoveCustomProperty(...)` or `ClearCustomProperties()` when a property should not be sent on future sessions.

## Host Pairing Memory

The runtime-owned host connection remembers successful host sessions by the Wi-Fi network name reported by the connected host. Each remembered profile stores the latest host/LAN address, host name, discovery metadata, and signed pairing config for that network.

Profiles expire after 14 days by default. A successful reconnect on the same reported Wi-Fi network refreshes the expiry timer and replaces the stored address/host metadata. Configure the retention window from the same options builder:

```csharp
var options = Options.CreateBuilder()
    .WithHostConnectionProfileRetention(TimeSpan.FromDays(30))
    .Build();
```

On startup and during host auto-probe, `HostConnectionRequest.Auto()` prefers embedded developer pairing when present, then cycles through each remembered connection profile newest-first, then tries saved config and bundled `ansight.json` fallback configs. Explicit `QrCode`, `PayloadText`, and `ConfigValue` requests still override the current host session and update the remembered profile after a successful connection.

## Developer Pairing

For local development, enable the developer-pairing MSBuild target in Debug builds:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <AnsightDeveloperPairingEnabled>true</AnsightDeveloperPairingEnabled>
</PropertyGroup>
```

When enabled, the build writes and embeds `ansight.developer-pairing.json`. If `ansight.json` exists in the project directory, the generated resource wraps that signed config with current local discovery metadata. If no source file exists, the generated resource is an `ansight.developer-pairing.v1` marker for Studio's development-only pairing path, so no checked-in pairing JSON is required.

Configure bundled host connection resources from the app assembly:

```csharp
var options = Options.CreateBuilder()
    .WithBundledHostConnection(typeof(AppBootstrap).Assembly)
    .Build();
```

For packaged text assets such as MAUI app assets, provide a shared loader for the standard asset names:

```csharp
var options = Options.CreateBuilder()
    .WithBundledHostConnection(
        (assetName, cancellationToken) => TryLoadBundledTextAssetAsync(assetName, cancellationToken),
        configReader: new MyHostConnectionConfigReader())
    .Build();
```

## Build-Time Remote Tool Enforcement

Ansight scans builds for concrete `Ansight.Tools.ITool` implementations unless remote tool scanning is explicitly bypassed.

Control build-time remote tool handling with `AnsightRemoteToolsPolicy`:

- `Allowed`: bypasses remote tool scanning, warnings, and detected-tool logging.
- `AllowedWithWarnings`: scans for remote tools, logs detected tool type and assembly details, emits a build warning when tools are present, and allows the build to continue. This is the default.
- `Disallowed`: scans for remote tools, logs detected tool type and assembly details, and fails the build when tools are present.

When the resolved policy is `Allowed` or `AllowedWithWarnings`, Ansight sets `AnsightRemoteToolsEnabled=true` and adds the `ANSIGHT_REMOTE_TOOLS` compile-time symbol. `Disallowed` sets `AnsightRemoteToolsEnabled=false` and omits that symbol.

For strict Release or CI builds, set:

```xml
<PropertyGroup>
  <AnsightRemoteToolsPolicy>Disallowed</AnsightRemoteToolsPolicy>
</PropertyGroup>
```

`Disallowed` will not work with the `Ansight` or `Ansight.Maui` all-in-one packages as-is because those packages intentionally include remote tools. To exercise this policy, use `Ansight.Core` plus the fine-grained `Ansight.Tools.*` packages and condition the tool references out of protected Release or CI builds.

Detected tool logging is enabled by default. To suppress the type and assembly list while keeping the selected policy behavior, set `AnsightLogRemoteTools=false`.

Use `Allowed` only when the build intentionally includes remote tools and you do not want build-time checks or warnings. Remote tools can expose screenshots, UI state, filesystem data, database contents, preferences, secure storage, and live runtime state to a connected host.

## Remote Tool Registration

`Ansight.Core` contains `ITool`, `ToolScope`, `ToolSchema`, `ToolDefinition`, `ToolRegistry`, `ToolResult`, and the `OptionsBuilder` registration methods. Each tool declares whether it is `Read`, `Write`, or `Delete`, plus explicit argument and result schemas for bridges such as MCP.

Concrete tool groups are delivered as separate packages and register through fluent extensions:

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
        secure.WithStorageIdentifier("ExampleApp");
        secure.AllowKey("session_token");
    })
    .WithReadWriteToolAccess()
    .Build();
```

Registered tools are guarded explicitly:

- `WithToolsDisabled()` disables discovery and execution.
- `WithReadOnlyToolAccess()` enables read tools.
- `WithReadWriteToolAccess()` enables read and write tools.
- `WithAllToolAccess()` enables all registered scopes.
- `WithToolGuard(...)` applies a custom policy.

Storage package remove operations and `files.delete_file` are delete-scoped, so `WithReadWriteToolAccess()` keeps them hidden and non-executable.

Reflection roots are the access boundary for `Ansight.Tools.Reflection`. Register a root with `ReflectionRootRegistry.Register(...)`, then the tools inspect reachable objects through stateless paths from that root. Direct object registrations are weak by default unless `ReferenceType.Strong` is passed; getter registrations use a `Func<object?>` when the exposed root can change over time and are unavailable while the getter returns `null`. Dispose the returned `ReflectionRootRegistrationHandle`, or call `ReflectionRootRegistry.Deregister(id)`, when a root should no longer be exposed.

When a `PairingSessionClient` WebSocket session is open, inbound `tool.query` and `tool.call` messages are processed automatically and answered on the same socket. Discovery and execution remain subject to the configured `ToolGuard`.

For file inspection, `Ansight.Tools.FileSystem` exposes `files.get_file_checksum` for sandboxed file fingerprints across `md5`, `sha1`, `sha256`, `sha384`, `sha512`, and `crc32`. For MCP-style extraction, `files.begin_binary_download` returns transfer metadata and then streams `ASFT` binary frames over the pairing WebSocket so the bridge can write bytes into a caller-chosen temp directory. `files.download_file` remains available as a JSON/base64 fallback.

## Individual Tool Packages

- `Ansight.Pairing`
- `Ansight.Tools.Maui`
- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Reflection`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`
- `Ansight.Tools.Preferences`
- `Ansight.Tools.SecureStorage`

## Supported Target Frameworks

- `net9.0-android`
- `net9.0-ios`
- `net9.0-maccatalyst`
