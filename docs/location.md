# Optional location capture

`Ansight.Location` records coordinates that the application has already obtained. It is an optional package on every SDK surface and uses the active Ansight session transport; it does not open a connection, request permission, install a delegate, subscribe to a platform location manager, or intercept location APIs.

Capture is disabled by default. Applications must both include the optional package and explicitly enable its recorder. Samples are labelled `app_observed`. Successful simulator/emulator commands recorded by the host are labelled `host_injected`; `correlationId` and `runId` let Studio compare the commanded and observed positions.

## Install and remove

| Surface | Optional dependency | Removal boundary |
| --- | --- | --- |
| .NET | `Ansight.Location`; add `Ansight.Location.Maui` only for the MAUI `Location` adapter | Remove the package reference and `WithObservedLocationCapture()` |
| Swift | SwiftPM product or CocoaPod `AnsightLocation` | Remove the product/pod and `import AnsightLocation` |
| Android | Maven artifact `ai.ansight:ansight-location-android` | Remove the Gradle dependency |
| React Native / Expo | `@ansight/react-native-location` | Remove the npm package and imports; there is no Expo config plugin |
| Flutter | `ansight_location` | Remove the pub dependency and import |
| Capacitor / Cordova | `@ansight/capacitor-location` | Remove the npm package and imports; there is no native plugin registration |

None of the aggregate Ansight packages depends on a location package. The optional packages add no permission or manifest entries. Excluded builds therefore contain no location registration, startup work, linker roots, native symbols, or runtime branches.

## Record an app-owned observation

### .NET and MAUI

```csharp
using Ansight.Location;

var ansightOptions = Ansight.Options.CreateBuilder()
    .WithAnsightMaui()
    .WithObservedLocationCapture(options => options
        .WithPrecision(4)
        .WithMinimumInterval(TimeSpan.FromSeconds(2))
        .WithMinimumDistance(5))
    .Build();

builder.UseAnsight(ansightOptions);

await LocationCapture.RecordAsync(new LocationSample
{
    Latitude = location.Latitude,
    Longitude = location.Longitude,
    HorizontalAccuracyMeters = location.Accuracy,
    CorrelationId = commandId,
    RunId = runId
});
```

For an app-owned `Microsoft.Maui.Devices.Sensors.Location`, reference `Ansight.Location.Maui` and call `MauiLocationCapture.RecordAsync(location, commandId, runId)`.

### Swift

```swift
import AnsightLocation

let recorder = AnsightLocationRecorder(options: .enabled(
    decimalPlaces: 4,
    minimumInterval: 2,
    minimumDistanceMeters: 5
))
await recorder.record(.init(
    latitude: location.coordinate.latitude,
    longitude: location.coordinate.longitude,
    horizontalAccuracyMeters: location.horizontalAccuracy,
    correlationId: commandID,
    runId: runID
))
```

`record(_:correlationId:runId:)` also accepts an app-owned `CLLocation`. Ansight never creates a `CLLocationManager`.

### Android

```kotlin
val recorder = AnsightLocationRecorder(
    AnsightLocationOptions.enabled(
        decimalPlaces = 4,
        minimumIntervalMilliseconds = 2_000,
        minimumDistanceMeters = 5.0,
    ),
)
recorder.record(appLocation, correlationId = commandId, runId = runId)
```

The `Location` must come from application code. The module does not add location permissions or register a listener.

### React Native / Expo

```ts
import { AnsightLocationRecorder } from '@ansight/react-native-location'

const recorder = new AnsightLocationRecorder({
  enabled: true,
  decimalPlaces: 4,
  minimumIntervalMilliseconds: 2000,
  minimumDistanceMeters: 5,
})
await recorder.recordExpoLocation(appLocation, { correlationId, runId })
```

### Flutter

```dart
final recorder = AnsightLocationRecorder(
  options: const AnsightLocationOptions(
    enabled: true,
    decimalPlaces: 4,
    minimumInterval: Duration(seconds: 2),
    minimumDistanceMeters: 5,
  ),
);
await recorder.recordCoordinates(
  latitude: appPosition.latitude,
  longitude: appPosition.longitude,
  correlationId: commandId,
  runId: runId,
);
```

### Capacitor and Cordova

```ts
import { AnsightLocationRecorder } from '@ansight/capacitor-location'

const recorder = new AnsightLocationRecorder({ enabled: true })
await recorder.recordGeolocationPosition(appPosition, { correlationId, runId })
```

## Privacy and sharing

Precision rounding, minimum interval, and minimum distance are applied before emission. Location stays in local session persistence and standard ZIP export/import. Cloud sharing removes all location samples from both metadata and the archive by default. Set `IncludeLocation = true` in `HostCloudSessionShareRequest`, or `includeLocation: true` in the cloud-sharing MCP tool, only after the user has intentionally approved sharing it.

The Studio/local replay map follows the session scrubber. Commanded points are orange, observed points are blue, and correlated pairs show their distance delta.
