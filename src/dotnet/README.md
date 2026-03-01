# Ansight — In-app performance monitoring for .NET mobile apps

[![Ansight](https://img.shields.io/nuget/vpre/Ansight.svg?cacheSeconds=3600&label=Ansight%20nuget)](https://www.nuget.org/packages/Ansight)

[![Ansight.Native](https://img.shields.io/nuget/vpre/Ansight.svg?cacheSeconds=3600&label=Ansight.Native%20nuget)](https://www.nuget.org/packages/Ansight.Native)

Monitor and visualise your .NET apps performance at runtime.

Use Ansight to identify and correalate memory leaks in your application and to track FPS drops the indicate performance issues.

**Why Ansight**
- View native memory usage, in app, while your app is running.
- Track your apps frame rate and detect performance drops.
- Simple, one liner integration.
- Natively integrated with very few dependencies (only SkiaSharp).

## Disclaimer ⚠️

Best effort has been made for performance and correctness, but Ansight continuously snapshots memory + FPS and stores recent samples in-memory; expect a small observer effect.

*Please treat Ansight’s numbers as a guidance, a heuristic.*

Always use the native tools and platform specific profilers (Xcode Instruments, Android Studio profiler) or `dotnet trace` for authoritative measurements.

## Quickstart

Pick the host style that suits your app.

### .NET for iOS, Android, and Mac Catalyst

1) Provide a presentation window (Android requires an `Activity`):
```csharp
// Android Activity
var options = Options.CreateBuilder()
    .WithPresentationWindowProvider(() => this) // required on Android (where 'this' is the current Activity)
    .Build();

Runtime.InitializeAndActivate(options);
```

On iOS or Mac Catalyst, the default window provider is used:
```csharp
Runtime.InitializeAndActivate();
```

2) Present Ansight in your UI:
```csharp
Runtime.PresentSheet();   // Slide-in sheet
Runtime.PresentOverlay(); // Window overlay
Runtime.DismissOverlay();
```

### .NET MAUI

On Android you must supply a delegate that returns the current activity so Ansight can attach its overlay.

1) Configure the app builder:
```csharp
// MauiProgram.cs
using Ansight;

var ansightOptions = Options.CreateBuilder()
  .WithMauiWindowProvider() // supplies the current Activity on Android
    .Build();

var builder = MauiApp.CreateBuilder()
  .UseMauiApp<App>()
  .UseAnsightAndActivate(ansightOptions); // or .UseAnsight(ansightOptions) then Runtime.Activate()
```

2) Show Ansight:
```csharp
Runtime.PresentSheet(); 
Runtime.DismissSheet();
Runtime.PresentOverlay();
Runtime.DismissOverlay();
```

## [Documentation](docs.md)

Full integration guide, platform notes, and runtime API walkthrough here: [docs.md](docs.md).

## What does Ansight capture?

### Android

| Metric | Description + Documentation |
|--------|-----------------------------|
| **Resident Set Size (RSS)** | Physical RAM currently mapped into the process (Java + native + runtime), excluding swapped pages. [Android Memory Overview](https://developer.android.com/topic/performance/memory-overview#mem-anatomy) • [`/proc` reference](https://man7.org/linux/man-pages/man5/proc.5.html) |
| **Native Heap** | Memory allocated through native allocators (`malloc`, `new`) used by the ART runtime and native libraries. [`Debug.getNativeHeapAllocatedSize`](https://developer.android.com/reference/android/os/Debug#getNativeHeapAllocatedSize) |
| **CLR (Managed Heap)** | Managed heap consumed by the .NET/Mono runtime (GC generations, LOH, objects, metadata). [.NET GC Fundamentals](https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals) |

### iOS/MacCatalyst

| Metric | Description + Documentation |
|--------|-----------------------------|
| **Physical Footprint (Jetsam Footprint)** | Total physical RAM attributed to the process by the kernel — the metric Jetsam uses to terminate apps. [`task_vm_info_data_t`](https://developer.apple.com/documentation/kernel/task_vm_info_data_t) • [WWDC Memory Deep Dive](https://developer.apple.com/videos/play/wwdc2018/416/) |
| **CLR (Managed Heap)** | Managed memory used by the .NET/Mono runtime on iOS (AOT GC heap + metadata). [.NET GC Fundamentals](https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals) |


## Limitations and Known Issues

### Modal Pages

When hosted inside MAUI, `WindowOverlay` attaches to the root window, so modal pages can obscure the overlay. Use the slide-in sheet (`PresentSheet`) for modal-heavy flows. 

On Android, the overlay is a transparent `FrameLayout` added to the current activity’s decor view; it stays on top of your main content but under system UI and will not be visible on modal pages. 

On iOS, a non-interactive `UIView` is injected into every active `UIWindow` (per scene); the overlay follows window bounds but will sit behind any OS-owned alerts or modal views.

### Only Supported on .NET 9 and higher

Ansight is explicitly built for .NET 9+ to leverage [`Span<T>` optimisations](https://learn.microsoft.com/en-us/dotnet/api/system.span-1?view=net-9.0), which enables some performance oriented code in the chart rendering, and [MAUI native embedding](https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-9?view=net-maui-10.0&utm_source=chatgpt.com#native-embedding), which enables Ansight's UIs to be built in MAUI but rendered inside native views.

As such, target frameworks earlier than .NET 9 are unsupported.
  
## Supported Platforms

| Platform | Status |
| --- | --- |
| .NET iOS | ✅ Supported |
| .NET Android | ✅ Supported |
| .NET Mac Catalyst | ✅ Supported |
| .NET MAUI | ✅ Supported |
| iOS Native | Planned |
| Android Native | Planned |
| React Native | Planned |
| Flutter | Planned |

If you would like support for additional platforms, email matthew@red-point.com.au to request support and express interest.

## Design Goals

**Minimal External Dependencies**

Ansight *must not* add undue dependencies to the integrating application.

As much as possible, Ansight must use the core .NET and platform APIs. Ansight’s only current external dependency is SkiaSharp.

**Minimal Overhead**

Ansight *should not* impact the performance of the integrating application.

Ansight should capture and present telemetry in the most efficient method possible and ensure it adds minimal memory overhead.

**Simple Integration**

Ansight *must* be simple for the integrating application to add and use across .NET for iOS, Android, Mac Catalyst, and MAUI.

Currently, Ansight can be added to an application in one line `.UseAnsightAndActivate()`.
