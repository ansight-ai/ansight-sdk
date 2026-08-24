# Ansight for Flutter

`ansight_flutter` adds Ansight observability and remote inspection to Flutter
applications. It combines the native Android and iOS runtimes with
Flutter-aware lifecycle, navigation, error, frame-timing, and widget-tree
instrumentation.

The package supports Flutter 3.0 or newer, Android API 24 or newer, and iOS
15 or newer.

For guarded startup and CLI verification, see the
[Flutter getting-started guide](https://www.ansight.ai/docs/sdk/flutter/setup).

## Install

Add the package to `pubspec.yaml`:

```yaml
dependencies:
  ansight_flutter: ^1.4.0-preview.1
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
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  if (kDebugMode) {
    await Ansight.instance.initializeAndActivate(
      AnsightOptions.developer(
        clientName: 'My Flutter App',
        toolGuard: AnsightToolGuard.readOnly,
      ),
    );
    await AnsightFlutterInstrumentation.instance.install();
  }

  runApp(const MyApp());
}
```

`install()` is idempotent. By default it captures Flutter errors, frame
timings, app lifecycle changes, and exposes Flutter widget inspection tools.
Its options can disable any of those integrations.

Start the local host in one terminal and leave it running:

```sh
ansight host run
```

Launch the development app, then verify the connected session and tool catalog
from another terminal:

```sh
ansight session list --connected --json
ansight app tools <session-id> --json
```

The native iOS Simulator or Android emulator runtime registers automatically
through loopback. No account, build-time host probe, or enrollment payload is
required.

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
  const AnsightChannel(id: 42, name: 'Queue depth'),
);
await Ansight.instance.metric(42, channel: 42);

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

## Network capture

Wrap the `package:http` client explicitly
used by the app:

```dart
final client = AnsightHttpClient(
  inner: http.Client(),
  sanitizationOptions: AnsightNetworkSanitizationOptions(
    maximumBodyBytes: 64 * 1024,
    additionalSensitiveHeaderNames: <String>['x-tenant-secret'],
    additionalSensitiveQueryParameterNames: <String>['session'],
    requestSanitizer: (request) =>
        request.url.contains('/health') ? null : request,
  ),
);

final response = await client.get(Uri.parse('https://api.example.test/orders'));
```

Import `package:http/http.dart` as `http` in the calling app. The wrapper is an
explicit opt-in and short-circuits directly to the inner client while no host is
connected. Text request and response bodies are included by default with a
64 KiB per-body limit; use `AnsightNetworkSanitizationOptionsBuilder` to disable
either side independently or configure a larger limit. Binary bodies remain an
explicit opt-in. The Dart and native sanitizers redact credentials, cloud
signed URLs, and sensitive text-body assignments before transport.

Call `Ansight.instance.recordNetworkRequest(...)` for another HTTP stack or a
manual record. `AnsightNetworkRequestSanitizer.sanitize(...)` exposes the
app-side sanitizer for inspection and tests.

Set the JPEG capture option mode to
`AnsightSessionJpegCaptureMode.screenshotWithVisualTreeOnTouch` to retain
periodic screenshots while the native runtime captures visual trees only on
touch down and touch up. Move and cancel events do not trigger capture. Rapid
boundaries are coalesced and rate-limited to protect screenshot cadence. Native
touch capture and visual-tree providers must remain enabled.

Open-file-handle and JNI reference-count diagnostics are disabled by default.
Enable them with `withOpenFileHandleTracking()` and
`withJniReferenceCountTracking()`; matching `without...` methods disable them
again. Open handles are sampled by the native Android/iOS runtime. JNI counts
are available on Android only when the host integration can supply them.

## Automatic session properties

The Dart bridge adds a `flutter` property group to every session with the
Ansight plugin version, Dart version, platform, build mode, JIT/AOT mode, and
development mode. The group identifies Dart as the runtime language. If the app
is compiled with a `FLUTTER_VERSION` Dart define, that value is included as
`flutterVersion`; Flutter does not otherwise expose its framework version or
active renderer through a stable runtime API.

It also adds a `localization` group containing the platform locale, language,
optional region, platform-reported time-zone name, and UTC offset in minutes.
`PlatformDispatcher` reports the platform locale, so an app that overrides its
locale in `MaterialApp` should override the corresponding `localization`
properties too.

Caller values win when they use the same group and key. Clearing session
properties, or removing one automatic property, restores the current
bridge-owned values.

## Enrollment and sessions

No connection call is needed for a simulator or emulator. For a physical
device, run `ansight pairing issue --qr`, then open the native scanner from a
developer-only app surface:

```dart
final result = await Ansight.instance.enrollFromQrCode(
  clientName: 'My Flutter App',
);
```

The native SDK supplies its real app id. No pairing file, prior app
registration, app-specific invite, build variable, or host address is required.
The first scan stores the app-installation registration; later launches
reconnect automatically.

If the app already owns a scanner, pass the current enrollment payload as a JSON
string or decoded JSON-compatible object:

```dart
final result = await Ansight.instance.openSession(
  enrollmentPayload,
  clientName: 'My Flutter App',
);
```

Saved enrollment and automatic runtime connection are available through
`AnsightOptions` and the connection APIs. Bundling an enrollment payload is not
part of normal setup.

Cellular host connections are disabled by default for QR scans, remembered
profiles, and explicit payload connections. Enable them only for a trusted
development host or personal hotspot:

```dart
final options =
    createOptionsBuilder().withCellularHostConnections().build();
```

This opt-in can consume mobile data and allows connection attempts over a
broader or carrier-managed network. Use it only with a trusted development
host.

## Custom tools

Flutter code can publish typed, policy-classified tools to a connected
Ansight host:

```dart
final registration = await Ansight.instance.registerTool(
  const AnsightToolDefinition(
    id: 'app.current_user',
    name: 'Current user',
    description: 'Returns the non-sensitive current user summary.',
    category: 'app',
    policy: AnsightToolPolicy.read,
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
