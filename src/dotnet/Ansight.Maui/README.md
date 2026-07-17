# Ansight.Maui

All-in-one Ansight package for .NET MAUI apps.

This package references `Ansight`, adds the MAUI remote tools, and provides `MauiAppBuilder` setup helpers with automatic MAUI telemetry wiring. `Ansight.Annotations` is included transitively but remains opt-in.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

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

Use `UseAnsight<App>()` to initialize and activate the runtime from the MAUI builder. It also automatically records foreground/background lifecycle transitions and records a screen-view event whenever a MAUI page appears. No `AppDelegate`, Android `Application`, or page `OnAppearing` calls are required for the default telemetry.

Enable the in-app feedback overlay explicitly in Debug builds:

```csharp
using Ansight.Annotations;

builder.UseAnsight<App>(ansight =>
{
    ansight.WithAnnotatedFeedback();
});

// From a button or other in-app action:
await Feedback.PresentAsync();
```

Annotated feedback is not enabled by `UseAnsight` or `WithAnsightMaui`, and `WithAnnotatedFeedback()` remains disabled when the consuming app is built in Release. A capture includes both the native and MAUI visual trees, even when a source is unavailable or disallowed.

> **Important:** The MAUI all-in-one defaults include session JPEG capture.
> Screen capture will result in an FPS drop while frames are captured, encoded,
> and transported. Disable session JPEG capture for performance-focused runs
> unless visual evidence is required.

```csharp
using Ansight.Maui;
using Ansight.Tools.SecureStorage;

builder.UseAnsight<App>(ansight =>
{
    ansight.WithAdditionalLogger(new CustomAnsightLogger());
    ansight.WithSecureStorageTools(secure =>
    {
        secure.WithStorageIdentifier("ExampleApp");
        secure.AllowKeyPrefix("ansight.secure.");
    });
});
```

The callback runs before default tool-suite registration. If it registers secure storage, preferences, MAUI tools, or another suite, the all-in-one setup skips the default registration for that suite and keeps the configured version. Full tool access is applied before the callback, so the callback can also narrow the guard.

Host auto-probe is enabled by the all-in-one defaults. Ansight remembers successful host sessions per Wi-Fi network reported by the connected host, stores the latest host/LAN address for that network, and retries those profiles so the app can reconnect after the host disappears and later reappears. Profiles expire after 14 days by default, and a successful reconnect refreshes the matching Wi-Fi profile. Configure retention in the same callback:

```csharp
builder.UseAnsight<App>(ansight =>
{
    ansight.WithHostConnectionProfileRetention(TimeSpan.FromDays(30));
});
```

If options are built manually, call the `MauiAppBuilder` overload with those options so the MAUI lifecycle and page-view hooks are registered:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightMaui()
    .Build();

builder
    .UseMauiApp<App>()
    .UseAnsight(options);
```

`WithAnsightMaui(...)` configures runtime defaults and MAUI tools only. `UseAnsight(...)` is the API that registers automatic foreground/background and `Application.PageAppearing` telemetry.

Remote tool scanning is controlled by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when tool packages are included. Because this all-in-one package intentionally includes remote tools, `Disallowed` will fail builds that reference it. Use `Ansight.Core` plus fine-grained `Ansight.Tools.*` references when you need protected Release or CI builds that exercise `Disallowed`. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
