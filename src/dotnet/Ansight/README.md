# Ansight

All-in-one Ansight package for .NET apps.

This package references `Ansight.Core`, native pairing where supported, and all non-MAUI remote tool packages. The runtime namespace remains `Ansight`.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

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

`WithAnsightSdk(...)` configures FPS sampling, 400ms sampling, 120s retention, 2000ms/quality-60/max-width-480 JPEG capture, host auto-probe, bundled host connection, all non-MAUI tools, and full tool access. Host auto-probe remembers successful host sessions per host-reported Wi-Fi network and retries those profiles so the app can reconnect after the host disappears and later reappears. Successful reconnects refresh the matching profile, and profiles expire after 14 days by default. Its callback receives the existing `Options.OptionsBuilder` after runtime defaults and before default tool-suite registration:

> **Important:** Screen capture will result in an FPS drop while frames are
> captured, encoded, and transported. Disable session JPEG capture for
> performance-focused runs unless visual evidence is required.

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

When the callback registers a tool suite, the all-in-one skips its default registration for that suite, so secure-storage and preferences access can be granted in the same builder call. Full tool access is applied before the callback, which lets the callback override the guard with `WithReadOnlyToolAccess()`, `WithReadWriteToolAccess()`, or `WithToolGuard(...)`.

Configure remembered host profile expiry in the same callback when the default 14 day retention is not appropriate:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithHostConnectionProfileRetention(TimeSpan.FromDays(30));
    })
    .Build();
```

Remote tool scanning is controlled by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when tool packages are included. Because this all-in-one package intentionally includes remote tools, `Disallowed` will fail builds that reference it. Use `Ansight.Core` plus fine-grained `Ansight.Tools.*` references when you need protected Release or CI builds that exercise `Disallowed`. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
