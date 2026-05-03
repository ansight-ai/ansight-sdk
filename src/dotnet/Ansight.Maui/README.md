# Ansight.Maui

All-in-one Ansight package for .NET MAUI apps.

This package references `Ansight`, adds the MAUI remote tools, and provides `MauiAppBuilder` setup helpers.

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

Use `UseAnsight<App>()` to initialize and activate the runtime from the MAUI builder. Use `WithAnsightMaui(...)` when manually building `Options`, or the existing `WithMauiTools()` extension when composing tool registration by hand.

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

Set `AnsightAllowRemoteTools=true` for builds that intentionally include tool packages.
