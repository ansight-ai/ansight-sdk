# Ansight

All-in-one Ansight package for .NET apps.

This package references `Ansight.Core`, native pairing where supported, and all non-MAUI remote tool packages. The runtime namespace remains `Ansight`.

```csharp
using Ansight;

var options = Options.CreateBuilder()
    .WithAnsight(ansight =>
    {
        ansight.WithBundledHostConnection(typeof(AppBootstrap).Assembly);
#if ANDROID
        ansight.WithPlatformPairing(() => CurrentActivityProvider());
#endif
    })
    .Build();

Runtime.InitializeAndActivate(options);
```

`WithAnsight(...)` configures FPS sampling, 400ms sampling, 120s retention, 2000ms/quality-60/max-width-600 JPEG capture, host auto-probe, bundled host connection, all non-MAUI tools, and full tool access. Its callback receives the existing `Options.OptionsBuilder` after runtime defaults and before default tool-suite registration:

```csharp
using Ansight;
using Ansight.Tools.SecureStorage;

var options = Options.CreateBuilder()
    .WithAnsight(ansight =>
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

Set `AnsightAllowRemoteTools=true` for builds that intentionally include tool packages.
