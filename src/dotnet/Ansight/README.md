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

`WithAnsightSdk(...)` configures FPS sampling, 400ms sampling, 120s retention, JPEG capture, host auto-probe, bundled host connection, all non-MAUI tools, and read-only tool access. Secure protocol-v2 sessions add signed discovery, pinned WSS, and per-install client-key authentication. Its callback receives the existing `Options.OptionsBuilder` after runtime defaults and before default tool-suite registration:

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

When the callback registers a tool suite, the all-in-one skips its default registration for that suite. Read-only tool access is applied before the callback; broader access requires both an explicit guard override and a matching authenticated protocol-v2 grant.

Configure remembered host profile expiry in the same callback when the default 14 day retention is not appropriate:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithHostConnectionProfileRetention(TimeSpan.FromDays(30));
    })
    .Build();
```

Remote tool scanning is controlled by `AnsightRemoteToolsPolicy`. Debug defaults to `AllowedWithWarnings`; Release defaults to `Disallowed` and also rejects developer pairing resources. Because this all-in-one package intentionally includes remote tools, protected Release builds should use `Ansight.Core` plus Debug-conditional fine-grained `Ansight.Tools.*` references.
