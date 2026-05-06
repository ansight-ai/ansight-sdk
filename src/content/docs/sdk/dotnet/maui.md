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

## Visual tree overlays

The all-in-one MAUI setup includes the generic visual tree tools from `Ansight.Tools.VisualTree`. Read tools such as `ui.get_visual_tree`, `ui.inspect_node`, `ui.get_screenshot`, `ui.get_overlay`, and `ui.query_overlays` are available when read-scoped tools are enabled.

Write-scoped overlay tools let a connected host draw temporary, input-transparent diagnostic boxes over the active native window:

- `ui.show_overlay` creates one or more highlight rectangles from explicit window coordinates, visual-tree coordinates, or a `nodeId`.
- `ui.update_overlay` edits an existing overlay by id. Omitted fields keep their current values.
- `ui.remove_overlay` removes one overlay by id.
- `ui.clear_overlays` removes all overlays, or only overlays matching a metadata key/value filter.

Overlays default to no fill, a red stroke, and a 5 second lifetime. Pass `durationMs=0` for an overlay that remains until removed. Use `fillColor=none` or `fillColor=transparent` to clear fill while editing. Each overlay can include a small scalar `metadata` dictionary so MCP clients can record why the overlay exists, such as a target node id, assertion name, or investigation step.

Overlay renderers are explicitly input-transparent and should not intercept taps, clicks, focus, or gestures. Enable these tools with `WithReadWriteToolAccess()` or a custom `ToolGuard`; `WithReadOnlyToolAccess()` intentionally does not allow creating, editing, or removing overlays.

## Host pairing memory

Host auto-probe is enabled by the MAUI all-in-one defaults. Ansight remembers successful host sessions per Wi-Fi network name reported by the connected host. Each remembered profile stores the latest host/LAN address, host name, discovery metadata, and signed pairing config for that network.

On startup and during automatic reconnects, Ansight tries embedded developer pairing first, then cycles through valid remembered Wi-Fi profiles newest-first, then falls back to saved configs and bundled `ansight.json` configs. Explicit QR, pasted payload, and config requests still override the current session and update the remembered Wi-Fi profile after a successful connection.

Remembered profiles expire after 14 days by default. A successful reconnect on the same reported Wi-Fi network refreshes the expiry timer and updates the stored address/host metadata. Configure the retention window in the MAUI setup callback:

```csharp
builder.UseAnsight<App>(ansight =>
{
    ansight.WithHostConnectionProfileRetention(TimeSpan.FromDays(30));
});
```
