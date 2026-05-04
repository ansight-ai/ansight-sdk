---
title: .NET MAUI Setup
description: Configure the Ansight.Maui all-in-one package and its automatic lifecycle and page-view telemetry.
---

Use `Ansight.Maui` for .NET MAUI apps. The all-in-one package includes the core runtime, host pairing, remote tools, MAUI inspection tools, and `MauiAppBuilder` setup helpers.

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

`UseAnsight<App>()` initializes and activates Ansight and registers automatic MAUI telemetry:

- foreground/background lifecycle transitions on Android, iOS, and Mac Catalyst
- screen-view events when `Application.PageAppearing` fires for a MAUI page

Apps do not need to call `Runtime.SetAppLifecycleState(...)` from platform delegates or `Runtime.ScreenViewed(...)` from each page for the default MAUI telemetry.

For custom runtime or tool options, configure the builder callback:

```csharp
using Ansight.Maui;
using Ansight.Tools.SecureStorage;

builder.UseAnsight<App>(ansight =>
{
    ansight.WithSecureStorageTools(secure =>
    {
        secure.WithStorageIdentifier("ExampleApp");
        secure.AllowKeyPrefix("ansight.secure.");
    });
});
```

When options are built manually, still pass them through the `MauiAppBuilder` extension so automatic MAUI telemetry is registered:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightMaui()
    .Build();

builder
    .UseMauiApp<App>()
    .UseAnsight(options);
```

`WithAnsightMaui(...)` configures runtime defaults and MAUI tools. `UseAnsight(...)` owns the MAUI app-builder integration, including lifecycle and page-view hooks.
