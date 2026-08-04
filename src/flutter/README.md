# Ansight for Flutter

`ansight_flutter` adds Ansight observability and remote inspection to Flutter
applications. It combines the native Android and iOS runtimes with
Flutter-aware lifecycle, navigation, error, frame-timing, and widget-tree
instrumentation.

The package supports Flutter 3.0 or newer, Android API 24 or newer, and iOS
15 or newer.

## Install

Add the package to `pubspec.yaml`:

```yaml
dependencies:
  ansight_flutter: ^1.2.0-preview.1
```

Then fetch dependencies:

```shell
flutter pub get
```

## Initialize

Initialize the native runtime before starting the app, then install the
Flutter instrumentation:

```dart
import 'package:ansight_flutter/ansight.dart';
import 'package:flutter/material.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  await Ansight.instance.initializeAndActivate(
    AnsightOptions.developer(
      clientName: 'My Flutter App',
      toolGuard: AnsightToolGuard.readOnly,
    ),
  );
  await AnsightFlutterInstrumentation.instance.install();

  runApp(const MyApp());
}
```

`install()` is idempotent. By default it captures Flutter errors, frame
timings, app lifecycle changes, and exposes Flutter widget inspection tools.
Its options can disable any of those integrations.

The native iOS Simulator or Android emulator runtime registers automatically
with a running, signed-in Studio. No build-time Studio probe or enrollment
payload is required.

Add the navigator observer to record route changes and screen views:

```dart
MaterialApp(
  navigatorObservers: <NavigatorObserver>[AnsightNavigatorObserver()],
  home: const HomePage(),
);
```

For production-specific configuration, construct `AnsightOptions` directly.
`AnsightOptions.developer()` enables the complete developer-oriented defaults;
keep the tool guard at the least-permissive level appropriate for the build.

## Telemetry

```dart
await Ansight.instance.registerMetricChannel(
  const AnsightChannel(id: 7, name: 'Queue depth'),
);
await Ansight.instance.metric(42, channel: 7);

await Ansight.instance.event(
  'Checkout completed',
  type: AnsightEventType.info,
  details: 'cart=primary',
);

await Ansight.instance.screenViewed(
  'Order details',
  details: <String, String>{'orderId': '123'},
);
```

The runtime also exposes built-in telemetry sampling, FPS control, screen-frame
capture, touch capture, session properties, custom properties, runtime
snapshots, recorded metrics/events, and log and connection-status streams.

## Pairing and sessions

No connection call is needed for a simulator or emulator. On a physical
device, scan the QR displayed by Studio once:

```dart
final result = await Ansight.instance.enrollFromQrCode(
  clientName: 'My Flutter App',
  expectedAppId: 'com.example.my_app',
);
```

No pairing file, build variable, or host address is required. The physical
device's first scan stores its app-installation registration; later launches
reconnect automatically.

For advanced paste, file-import, CI, or custom-UI flows, pairing payloads may be
JSON strings or decoded JSON-compatible objects:

```dart
final result = await Ansight.instance.openSession(
  pairingPayload,
  clientName: 'My Flutter App',
  expectedAppId: 'com.example.my_app',
);
```

Saved enrollment, automatic runtime connection, and host-address overrides are
available through `AnsightOptions` and the connection APIs. Bundling an
enrollment payload is not part of the normal setup.

Cellular host connections are disabled by default for bundled configs, QR
scans, remembered/saved profiles, and manual connections. Enable them only for
a trusted development host or personal hotspot:

```dart
final options =
    createOptionsBuilder().withCellularHostConnections().build();
```

This opt-in can consume mobile data and allows connection attempts over a
broader or carrier-managed network. Use it only with a trusted development
host.

## Custom tools

Flutter code can publish typed, security-described tools to a connected
Ansight host:

```dart
final registration = await Ansight.instance.registerTool(
  const AnsightToolDefinition(
    id: 'app.current_user',
    name: 'Current user',
    description: 'Returns the non-sensitive current user summary.',
    category: 'app',
    scope: AnsightToolScope.read,
    security: AnsightToolSecurity(
      level: AnsightToolSecurityLevel.low,
    ),
  ),
  (arguments, context) async => const AnsightToolResult.success(
    result: <String, Object?>{'signedIn': true},
  ),
);

// Later:
await registration.unregister();
```

`AnsightArtifactProvider` supports discoverable text or binary artifacts.
`queueBinaryTransfer()` is available for chunked native transfer of larger
payloads.

## Platform configuration

Android requires `minSdkVersion 24` or newer and an app using Flutter's Android
embedding v2. The plugin compiles against Android API 34.

iOS requires a deployment target of 15.0 or newer. Add a local-network usage
description because development pairing connects to the Ansight host:

```xml
<key>NSLocalNetworkUsageDescription</key>
<string>Connect to the Ansight developer host on the local network.</string>
```

## Harness and validation

The package includes a feature-complete app in `example/`. It exercises
runtime state, all telemetry types, pairing and sessions, screenshots, touch
capture, widget tools, navigation, errors, custom tools, artifacts, binary
transfer, properties, options, capabilities, and logs.

Run the package and harness checks with:

```shell
flutter test
(cd example && flutter test)
flutter test example/integration_test -d <device-id>
flutter build apk --debug --target example/lib/main.dart
flutter build ios --simulator --no-codesign --target example/lib/main.dart
```

`tool/flutter_corpus.dart` integrates and validates the SDK against the
repository's 22-app open-source Flutter corpus. Its generated JSON, Markdown,
and command logs provide reproducible build evidence.

Regenerate the Pigeon transports with `dart run tool/generate_pigeon.dart`.
That wrapper reapplies the small compatibility transforms required by Flutter
3.0 after Pigeon emits its current Flutter, Kotlin, and Swift sources.

## License

This SDK is source-available software, not open-source software. See
`LICENSE` for the permitted uses and restrictions.
