# Install once, run

Ansight's default development connection flow is:

1. Install the SDK and initialize its developer preset.
2. Run the app while Ansight Studio is open and signed in.

Simulators, emulators, Mac Catalyst apps, and desktop apps register
automatically through loopback. There is no pairing config, Studio build probe,
generated file, certificate, signing key, host address, build constant, or
approval service to set up. If Studio is closed or signed out, the SDK remains
dormant and retries later without failing or delaying the app.
Host-local discovery checks the supported installed and source-build Studio
ports with short loopback-only timeouts before considering older stored state.

A physical phone cannot use the host's loopback interface. Open Studio's
**Pair Any App** screen and scan its generic one-use QR. No app entry or
app-specific invite is created first: the scanning SDK sends its runtime app
identity, Studio registers it after authorization, and the SDK saves the
app-scoped registration in private storage for later reconnects.

## Android

Install the aggregate Android artifact and initialize the runtime:

```kotlin
Ansight.initializeAndActivate(application)
```

Android emulators connect automatically. For a physical device, invoke
`Ansight.enrollFromQrCode(activity)` from a developer-only screen. Google Code
Scanner owns the camera interaction, so the host app does not need to request
`CAMERA`. The SDK declares only the ordinary network permissions it uses and
does not change the host app's `usesCleartextTraffic` or network-security
policy.

Kotlin source projects need Kotlin Gradle plugin 1.8 or newer.

## iOS

Install the aggregate `Ansight` Swift package:

```swift
try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
```

iOS Simulator and Mac Catalyst connect automatically. A physical iPhone uses
`await AnsightRuntime.shared.connect(.qrCode(...))` once. The SDK-owned scanner
requires `NSCameraUsageDescription`, and direct LAN access triggers Apple's
Local Network privacy control. Ansight does not request Bluetooth, location,
contacts, photos, or Bonjour discovery.

## React Native

```ts
await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: __DEV__,
});
```

The native simulator/emulator runtime connects automatically. Call
`Ansight.enrollFromQrCode()` only on a physical device.

Expo apps use the same `@ansight/react-native` package. Add
`@ansight/react-native` to the app config `plugins` array, then create an Expo
development or EAS build so the native module and iOS permission descriptions
are included. Expo Go and Expo Web are not supported. See the
[React Native SDK guide](../src/react-native/README.md#expo-development-builds).

## Capacitor

```ts
await Ansight.initializeAndActivate(
  Ansight.createOptionsBuilder().withAnsightDefaults().build(),
);
```

The native simulator/emulator runtime connects automatically. Call
`Ansight.enrollFromQrCode()` only on a physical device.

## Flutter

```dart
await Ansight.instance.initializeAndActivate(AnsightOptions.developer());
await AnsightFlutterInstrumentation.instance.install();
```

The native simulator/emulator runtime connects automatically. Call
`Ansight.instance.enrollFromQrCode()` only on a physical device.

## .NET and MAUI

Initialize the all-in-one SDK during app startup. Its platform pairing reader
opens the native enrollment scanner:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .Build();

Runtime.InitializeAndActivate(options);
```

Simulator and Mac Catalyst targets connect automatically through the native
bridge. Use `HostConnectionRequest.QrCode()` once for a physical device. The
exact initialization entry point varies between the portable, MAUI, and native
binding packages, but all use the same runtime enrollment protocol.

For an unattended physical-device test build, add
`.WithUnattendedProvisioning()`. The host runner can then inject a one-use
enrollment payload at process launch: `ANSIGHT_ENROLLMENT_PAYLOAD` on iOS, or
the `ai.ansight.bootstrap.payload` launcher Intent extra on Android. The option
is disabled by default and successful enrollment is remembered privately by
the app installation.

## Network model

Enrollment uses UDP followed by `ws://`. Local automatic enrollment is accepted
only from loopback and only while Studio is signed in. Physical-device
enrollment uses a one-use bearer invite over the local network. No certificate
is needed. Traffic is not encrypted or authenticated against an active network
attacker, so use physical-device connections only on a network you trust.

Android manifest integration is automatic. The iOS client uses
Network.framework directly, so it does not need an ATS clear-text exception.
iOS camera and Local Network privacy descriptions cannot be injected into the
final app by Swift Package Manager, so the app must provide the descriptions
when it invokes those features.

## When a physical device needs a fresh scan

Scan a new QR when:

- this physical installation has never registered;
- app storage was cleared or the app was reinstalled;
- the registration expired or Studio revoked it;
- a different phone tries to use an invite that was already consumed.

The generic QR is claimed by the first scanning app installation. The
registered phone does not need to rescan merely because the original QR later
expires.
