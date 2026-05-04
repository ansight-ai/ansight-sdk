# Ansight .NET SDK

Ansight provides in-process telemetry, host pairing, live JPEG capture, and guarded remote tools for .NET Android, iOS, and Mac Catalyst apps.

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
- live JPEG capture every 2000ms at quality 60 and max width 600
- host auto-probe enabled
- bundled host connection configured from the entry assembly, or overridden through `WithBundledHostConnection(...)`
- all non-MAUI remote tools registered
- full tool access enabled

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
    .WithSessionJpegCapture(intervalMilliseconds: 2000, quality: 60, maxWidth: 720)
    .WithBundledHostConnection(typeof(AppBootstrap).Assembly)
    .Build();

Runtime.InitializeAndActivate(options);
```

Install `Ansight.Core` for this lower-level surface. Add `Ansight.Pairing` separately if the app should own native QR acquisition while staying on the core package set.

## Build-Time Remote Tool Enforcement

Ansight scans builds for concrete `Ansight.Tools.ITool` implementations unless remote tool scanning is explicitly bypassed.

Control build-time remote tool handling with `AnsightRemoteToolsPolicy`:

- `Allowed`: bypasses remote tool scanning, warnings, and detected-tool logging.
- `AllowedWithWarnings`: scans for remote tools, logs detected tool type and assembly details, emits a build warning when tools are present, and allows the build to continue. This is the default.
- `Disallowed`: scans for remote tools, logs detected tool type and assembly details, and fails the build when tools are present.

For strict Release or CI builds, set:

```xml
<PropertyGroup>
  <AnsightRemoteToolsPolicy>Disallowed</AnsightRemoteToolsPolicy>
</PropertyGroup>
```

`Disallowed` will not work with the `Ansight` or `Ansight.Maui` all-in-one packages as-is because those packages intentionally include remote tools. To exercise this policy, use `Ansight.Core` plus the fine-grained `Ansight.Tools.*` packages and condition the tool references out of protected Release or CI builds.

Detected tool logging is enabled by default. To suppress the type and assembly list while keeping the selected policy behavior, set `AnsightLogRemoteTools=false`.

Use `Allowed` only when the build intentionally includes remote tools and you do not want build-time checks or warnings. Remote tools can expose screenshots, UI state, filesystem data, database contents, preferences, secure storage, and live runtime state to a connected host.

## Individual Tool Packages

- `Ansight.Pairing`
- `Ansight.Tools.Maui`
- `Ansight.Tools.VisualTree`
- `Ansight.Tools.Reflection`
- `Ansight.Tools.Database`
- `Ansight.Tools.FileSystem`
- `Ansight.Tools.Preferences`
- `Ansight.Tools.SecureStorage`
