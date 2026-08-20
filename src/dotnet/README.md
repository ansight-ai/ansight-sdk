# Ansight .NET SDK

Ansight provides in-process telemetry, host pairing, live JPEG capture, and guarded remote tools for .NET Android, iOS, and Mac Catalyst apps.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Package Model

- `Ansight.Core`: core runtime, telemetry, host pairing protocol, tool abstractions, and build-time safety targets.
- `Ansight.Annotations`: opt-in, Debug-only in-app feedback, screenshot, visual-tree, hook, artifact, and bundle delivery support.
- `Ansight.OfflineCapture`: offline telemetry, event, touch, screenshot, and annotation storage with retention, ZIP/AES export, and team upload.
- `Ansight`: all-in-one package for non-MAUI .NET apps. It depends on `Ansight.Core`, bundles annotations and offline capture without enabling either workflow, includes native pairing where supported, and includes all non-MAUI remote tool packages.
- `Ansight.Maui`: all-in-one package for .NET MAUI apps. It depends on `Ansight`, bundles annotations and offline capture without enabling either workflow, and adds MAUI inspection/mutation tools plus `MauiAppBuilder` setup helpers with automatic lifecycle and page-view telemetry.
- `Ansight.Tools.*`: individual tool packages for apps that want explicit package-by-package control.

The runtime namespace remains `Ansight` even when the NuGet package is `Ansight.Core`.

## Mobile Native Runtime

On Android, iOS, and Mac Catalyst, the .NET packages include their native
binding automatically. Kotlin owns the Android runtime and Swift owns the Apple
runtime. The native runtime is the single owner of activation, lifecycle and
capture hooks, secure saved registration, host auto-connect, telemetry
retention, the live WebSocket, and binary transfers.

The .NET layer supplies the public C# API, CLR heap samples, MAUI integration,
and managed tool implementations. Tool requests cross the bridge for execution,
but their replies and binary payloads return through the native runtime's
existing connection. It does not open a parallel managed pairing session.
`Runtime.Instance.DataSink` is a live .NET projection of the native telemetry
buffer, so existing snapshot, event, and offline-capture consumers continue to
work against the same recorded data.

The bridge packages are implementation details; applications do not install or
configure them separately. Install the SDK and initialize it. Simulator,
Mac Catalyst, and desktop runtimes register automatically with a running,
signed-in Studio; physical devices scan the generic one-use QR from Studio's
**Pair Any App** screen and then reconnect from app-private registration state.
The runtime app id is registered automatically after the scan.

## All-In-One

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .Build();

Runtime.InitializeAndActivate(options);
```

The all-in-one package starts runtime enrollment automatically and registers
platform QR support for physical devices, including current Android activity
tracking. No pairing file, MSBuild property, host address, Studio build probe,
or activity-provider callback is required.

The all-in-one packages include annotations but do not enable them by default. To expose an in-app feedback action, opt in explicitly; the runtime activates it only for a Debug application build:

```csharp
using Ansight.Annotations;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight => ansight.WithAnnotatedFeedback())
    .Build();

Runtime.InitializeAndActivate(options);
await Feedback.PresentAsync();
```

The all-in-one packages also reference `Ansight.OfflineCapture`, but capture
does not start until the app calls `OfflineCapture.Configure(...)`,
`InitializeAsync()`, and an explicit or persisted activation path. See
[Ansight.OfflineCapture](Ansight.OfflineCapture/README.md) for retention,
encrypted export, annotation-bundle storage, and team upload.

`WithAnsightSdk(...)` applies the same practical defaults that Redpoint has been using:

- FPS sampling enabled
- 400ms sample frequency
- 120s retention
- live JPEG capture every 2000ms at quality 60 and max width 480
- host auto-probe enabled
- automatic host-local registration and remembered physical-device enrollment
- all non-MAUI remote tools registered
- full tool access enabled

Cellular host connections remain disabled in every preset. To allow a personal
hotspot or another cellular path, add `.WithCellularHostConnections()` to the
options builder. The opt-in applies to bundled configs, QR scans, saved
profiles, and manual connections. It can consume mobile data and broaden
network exposure, so use it only with a trusted development host.

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

Set `SessionJpegCaptureOptions.CaptureGpuBackedSurfaces` to `false` to use the
lower-overhead capture path on supported Apple platforms when Metal or SceneKit
content is not required. It defaults to `true`.

Set the mode to `SessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch` to
keep periodic screenshots but capture visual trees around interaction instead:

```csharp
var options = Options.CreateBuilder()
    .WithSessionJpegCapture(mode: SessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch)
    .WithTouchCapture()
    .Build();
```

The runtime captures at gesture start, every 250 ms throughout the gesture,
and at its final up or cancel. At least one session visual-tree provider must
be registered.

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

Runtime diagnostics are opt-in and disabled by default. Use
`WithOpenFileHandleTracking()` on Android or Apple platforms and
`WithJniReferenceCountTracking()` on Android. Open handles are sampled by the
native platform runtime, while the Android JNI count comes from Java.Interop's
tracked global references and is recorded into the native telemetry channel.
Matching `Without...` methods support copied or reused builders.

Android builds using `WithAnsightSdk()` also register
`jni_references.capture_graph`. Lower-level Android integrations can register
it explicitly with `WithJniReferenceDiagnosticsTools()`. The tool returns a
bounded, redacted JNI-rooted object graph; it does not return object addresses
or field values. Studio promotes the full result to a timeline artifact. JNI
reference-count telemetry remains the lightweight path for continuous
monitoring, while graph capture is an explicit, potentially expensive heap
diagnostic that briefly pauses the app.

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

### App artifact providers

`Ansight.Core` can expose app-defined snapshots such as logs, reports, traces,
images, or state exports. Implement `IArtifactProvider` and register it while
building options:

```csharp
using Ansight.Artifacts;

var options = Options.CreateBuilder()
    .AddArtifactProvider(new CurrentReportArtifactProvider())
    .WithReadOnlyToolAccess()
    .Build();
```

The provider describes its currently available exports from `QueryAsync(...)`
and creates one requested snapshot from `CreateAsync(...)`. Use
`ArtifactPayload.FromText(...)`, `FromBytes(...)`, `FromStream(...)`, or
`FromFile(...)` for the returned payload. Registering a provider adds the
read-scoped `artifacts.query` and `artifacts.request` tools automatically.
Artifact bytes are streamed over the active pairing session, so requests
require a live Studio connection. The shared result shape and error codes are
documented in [Artifact Tools](../../docs/protocol.md#artifact-tools).

## Host Enrollment Memory

The runtime-owned host connection remembers successful host sessions by the Wi-Fi network name reported by the connected host. Each remembered profile stores the latest host/LAN address, host name, discovery metadata, and app-installation registration for that network.

Profiles expire after 14 days by default. A successful reconnect on the same reported Wi-Fi network refreshes the expiry timer and replaces the stored address/host metadata. Configure the retention window from the same options builder:

```csharp
var options = Options.CreateBuilder()
    .WithHostConnectionProfileRetention(TimeSpan.FromDays(30))
    .Build();
```

`HostConnectionRequest.Auto()` uses the remembered app-installation
registration. Background host auto-probe retries that state while the runtime
is active. `QrCode` is the normal first-use path; `PayloadText` supports apps
that already own the scanner UI.

For unattended physical-device test runs, opt in only for development or test
builds:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .WithUnattendedProvisioning()
    .Build();
```

At launch, the native iOS bridge consumes `ANSIGHT_ENROLLMENT_PAYLOAD` from the
process environment. The Android bridge consumes the
`ai.ansight.bootstrap.payload` string extra from the launcher Activity Intent.
The runner must supply a fresh, app-specific, one-use enrollment payload. On a
successful connection, the native SDK saves the resulting app-installation
registration in platform-private storage for later automatic reconnects.

Host auto-probe is enabled by default while the runtime is active. It remembers previous host connections and retries them so the app can reconnect after the host disappears and later reappears. Customize the retry behavior when you need a different client name or cadence:

```csharp
var options = Options.CreateBuilder()
    .WithHostAutoProbe(new HostAutoProbeOptions
    {
        InitialDelay = TimeSpan.FromSeconds(1),
        ProbeInterval = TimeSpan.FromSeconds(5),
        ReconnectDelay = TimeSpan.FromSeconds(10),
        ClientName = "My .NET App"
    })
    .Build();
```

Use `WithoutHostAutoProbe()` for flows where reconnects should only happen after an explicit app action.

## Enrollment setup

Install the SDK and initialize it. Host-local runtimes enroll automatically
while Studio is open and signed in. Open `HostConnectionRequest.QrCode()` only
from a physical device's developer-only surface. No build target, Studio build
probe, pairing file, certificate, signing key, or host address is required.

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

Reflection roots are the access boundary for `Ansight.Tools.Reflection`. Register a root with `ReflectionRootRegistry.Register(...)`, then the tools inspect reachable objects through stateless paths from that root. `reflect.list_roots` includes a `hostRuntime` descriptor so callers can identify CLR-hosted roots. Direct object registrations are weak by default unless `ReferenceType.Strong` is passed; getter registrations use a `Func<object?>` when the exposed root can change over time and are unavailable while the getter returns `null`. Dispose the returned `ReflectionRootRegistrationHandle`, or call `ReflectionRootRegistry.Deregister(id)`, when a root should no longer be exposed.

When a `PairingSessionClient` WebSocket session is open, inbound `tool.query` and `tool.call` messages are processed automatically and answered on the same socket. Discovery and execution remain subject to the configured `ToolGuard`.

For file inspection, `Ansight.Tools.FileSystem` exposes `files.get_file_checksum` for sandboxed file fingerprints across `md5`, `sha1`, `sha256`, `sha384`, `sha512`, and `crc32`. For MCP-style extraction, `files.begin_binary_download` returns transfer metadata and then streams `ASFT` binary frames over the pairing WebSocket so the bridge can write bytes into a caller-chosen temp directory. `files.download_file` remains available as a JSON/base64 fallback.

## Individual Packages

- `Ansight.Annotations`
- `Ansight.OfflineCapture`
- `Ansight.Pairing`
- `Ansight.Tools.Maui`
- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Reflection`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`
- `Ansight.Tools.Preferences`
- `Ansight.Tools.SecureStorage`

## Supported Target Frameworks

- `net9.0` for platform-neutral runtime, capture/export, and tool composition
- `net9.0-android`
- `net9.0-ios`
- `net9.0-maccatalyst`
