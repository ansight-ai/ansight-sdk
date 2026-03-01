# Ansight Integration and Builder Guide

Ansight is a .NET-native, in-app performance tracker for .NET for iOS, Android, Mac Catalyst, and .NET MAUI. This guide covers integration details, runtime APIs, and advanced configuration beyond the README quickstart.

## Platform setup

### Native .NET for Android

Provide the activity or window Ansight should use for presenting its overlay:

```csharp
// In your Activity
var options = Options.CreateBuilder()
    .WithPresentationWindowProvider(() => this) // required on Android
    .Build();

Runtime.InitializeAndActivate(options);
```

The delegate should return the current foreground activity; Ansight will throw if `PresentationWindowProvider` is missing on Android.

### Native .NET for iOS / Mac Catalyst

The default window provider uses the key `UIWindow`, so you can initialise without extra configuration:

```csharp
Runtime.InitializeAndActivate();
```

Provide a custom window via `WithPresentationWindowProvider` if you need to target a non-key scene or window.

### .NET MAUI host

MAUI on Android must supply a delegate that returns the current activity; `WithMauiWindowProvider` wires up `Platform.CurrentActivity` for you.

```csharp
// MauiProgram.cs
using Ansight;

var ansightOptions = Options.CreateBuilder()
    .WithMauiWindowProvider() // required on Android
    .Build();

var builder = MauiApp.CreateBuilder()
    .UseMauiApp<App>()
    .UseAnsight(ansightOptions);

Runtime.Activate(); // or builder.UseAnsightAndActivate(ansightOptions)
```

If you provide your own delegate, pass it via `WithPresentationWindowProvider(() => Platform.CurrentActivity)`. Without one, MAUI/Android will throw during startup.

### Presenting Ansight

Once initialised and activated, present or dismiss Ansight from anywhere in your app:

```csharp
Runtime.PresentSheet();
Runtime.DismissSheet();

Runtime.PresentOverlay();
Runtime.DismissOverlay();
```

## Record Events

Record markers and additional metrics so memory spikes have context.

```csharp
// Define channels first (avoid reserved IDs 0, 1 and 255).
var channels = new []
{
    new Channel(96, "Image Cache", Colors.Orange),
    new Channel(97, "Network Buffers", Colors.Green)
};

// Initialise Ansight with those channels.
var options = Options.CreateBuilder()
    .WithAdditionalChannels(channels)
    .Build();
Runtime.InitializeAndActivate(options);

// Add metrics (rendered as extra series).
Runtime.Metric(currentCacheSizeBytes, 96);

// Add events (rendered as vertical markers + items in the event list).
Runtime.Event("Cache cleared", 96);                    // default type + icon "*"
Runtime.Event("GC requested", AppEventType.Gc);      // GC event symbol "g"
Runtime.Event("Large download", AppEventType.Event, 97, "42 MB");
```

Events/metrics on unknown channels are ignored. Both the slide-in sheet and overlay display the channels and event markers, letting you correlate spikes with the moments you annotated.

## Customize Ansight

Use the `OptionsBuilder` to tune sampling, channels, gestures and logging:

```csharp
var options = Options.CreateBuilder()
    .WithSampleFrequencyMilliseconds(500)     // clamp: 200–2000 ms
    .WithRetentionPeriodSeconds(10 * 60)      // clamp: 60–3600 s
    .WithAdditionalChannels(customChannels)   // extra metric/event series
    .WithDefaultMemoryChannels(DefaultMemoryChannels.PlatformDefaults) // iOS/Android sensible defaults
    .WithShakeGesture()                       // enable shake-to-toggle
    .WithDefaultOverlayPosition(OverlayPosition.TopRight) // default anchor when showing overlay without an explicit position
    .WithShakeGestureBehaviour(ShakeGestureBehaviour.Overlay) // or SlideSheet
    .WithEventRenderingBehaviour(AppEventRenderingBehaviour.IconsOnly) // LabelsAndIcons, IconsOnly (default), None
    .WithChartTheme(ChartTheme.Light)    // Light or Dark (default)
    .WithAdditionalLogger(new MyLogger())     // or .WithBuiltInLogger()
    .WithPresentationWindowProvider(() => /* your Android Activity or window */)
    .WithSaveSnapshotAction((snapshot) => Persist(snapshot), "SAVE")
    .Build();
```

Use `WithEventRenderingBehaviour` (or change `Runtime.AppEventRenderingBehaviour` at runtime) to choose between icons with labels, icons only, or hiding events entirely. This applies to both the slide sheet and overlay chart.

Switch themes on the fly by updating `Runtime.ChartTheme`:

```csharp
Runtime.ChartTheme = ChartTheme.Light; // or Dark
```


### Control the Default Memory Metrics

`Options` now exposes `WithDefaultMemoryChannels` and `WithoutDefaultMemoryChannels` so you can decide which of Ansight's built-in metrics appear. The flags in `DefaultMemoryChannels` cover the CLR managed heap, Android's native heap, Android's RSS, and iOS's physical footprint channel.

```csharp
var options = Options.CreateBuilder()
    .WithDefaultMemoryChannels(DefaultMemoryChannels.ManagedHeap) // managed heap only
    .Build();

var hideNoise = Options.CreateBuilder()
    .WithoutDefaultMemoryChannels(DefaultMemoryChannels.NativeHeap | DefaultMemoryChannels.ResidentSetSize)
    .Build();
```

By default Ansight enables the platform-appropriate channels (CLR + Native + RSS on Android, CLR + Physical Footprint on iOS). Supplying `DefaultMemoryChannels.None` hides every built-in memory series so you can overlay only your custom channels.

### On-demand Shake Predicate

When you need to gate the shake gesture behind your own runtime configuration, provide a predicate. Ansight consults it before activating the listener and each time a shake occurs, so you don't have to manually call `EnableShakeGesture` or `DisableShakeGesture` as your config changes.

```csharp
var options = Options.CreateBuilder()
    .WithShakeGesture()
    .WithShakeGesturePredicate(() => MyDebugConfig.IsShakeAllowed)
    .Build();
```

If the predicate returns `false`, the accelerometer remains registered but shakes are ignored until the predicate later returns `true`. If it throws, Ansight logs the exception and suppresses the shake.

## Eager initialisation

While the MAUI app builder extension registers Ansight for you, you may want sampling to start before your UI loads:

- **Android**: initialise inside `MainApplication` so the runtime is ready before `CreateMauiApp()` or your first activity starts.
- **iOS/macOS Catalyst**: initialise before `UIApplication.Main` in `Program.cs`:

```csharp
var options = /* build options */;
Runtime.InitializeAndActivate(options);
UIApplication.Main(args, null, typeof(AppDelegate));
```

## FPS Tracking

Ansight can sample frames-per-second alongside memory metrics and overlay the results on the chart. Enable FPS capture when building options:

```csharp
var options = Options.CreateBuilder()
    .WithFramesPerSecond()
    .Build();
```

At runtime you can call `Runtime.EnableFramesPerSecond()` or `.DisableFramesPerSecond()` to toggle sampling without rebuilding the options. FPS series segments automatically change color as the rate crosses the built-in thresholds (Optimal ≥50, Stable 40–49, Fair 30–39, Poor 20–29, Critical <20) so jank is easy to spot.
