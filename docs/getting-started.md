# Install once, scan once

Ansight's default development connection flow is:

1. Install the SDK and initialize its developer preset.
2. Show the enrollment QR in Ansight Studio.
3. Scan it once on the phone.
4. Launch the app normally from then on.

The first scan registers a random app-installation id and saves the registration
in app-private storage. Later launches reconnect automatically while that
registration remains valid.

There is no pairing config, certificate, signing key, host address, copied
file, build constant, or approval service to set up. The QR contains the
one-use enrollment secret and Studio's current LAN addresses.

## Android

Install the aggregate Android artifact, initialize the runtime, and invoke the
scanner from a developer-only screen:

```kotlin
Ansight.initializeAndActivate(application)
Ansight.enrollFromQrCode(activity)
```

The SDK declares the ordinary network permissions it actually uses. Its
developer WebSocket transport does not change the host app's
`usesCleartextTraffic` or network-security policy. Google Code Scanner performs
the scan, so the host app does not need to request `CAMERA`.

Kotlin source projects need Kotlin Gradle plugin 1.8 or newer.

## iOS

Install the aggregate `Ansight` Swift package:

```swift
try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
await AnsightRuntime.shared.connect(.qrCode(title: "Scan Ansight Enrollment QR"))
```

An SDK-owned camera scanner requires `NSCameraUsageDescription`. A physical
iPhone connecting directly to Studio also triggers Apple's Local Network
privacy control. These are required by iOS for the features being used; Ansight
does not request Bluetooth, location, contacts, photos, or Bonjour discovery.

## React Native

```ts
await Ansight.initializeAndActivate({
  useNativeAllInOneDefaults: __DEV__,
});
await Ansight.enrollFromQrCode();
```

## Capacitor

```ts
await Ansight.initializeAndActivate(
  Ansight.createOptionsBuilder().withAnsightDefaults().build(),
);
await Ansight.enrollFromQrCode();
```

## Flutter

```dart
await Ansight.instance.initializeAndActivate(AnsightOptions.developer());
await AnsightFlutterInstrumentation.instance.install();
await Ansight.instance.enrollFromQrCode();
```

## .NET and MAUI

Initialize the all-in-one SDK during app startup. Its platform pairing reader
opens the native enrollment scanner:

```csharp
var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .Build();

Runtime.InitializeAndActivate(options);
await Runtime.HostConnection.ConnectAsync(
    HostConnectionRequest.QrCode());
```

The exact runtime entry point varies between the portable, MAUI, and native
binding packages, but they all use the same stored installation id and
enrollment protocol.

## Network model

Enrollment uses UDP followed by `ws://` on the local network. No certificate is
needed. This is the lowest-friction path for local development, but traffic is
not encrypted or authenticated against an active network attacker. Use it only
on a network you trust.

Android manifest integration is automatic. The iOS client uses
Network.framework directly, so it does not need an ATS clear-text exception.
iOS camera and Local Network privacy descriptions cannot be injected into the
final app by Swift Package Manager, so the app must provide the descriptions
when it invokes those features.

## When a fresh scan is required

Scan a new QR when:

- this installation has never registered;
- app storage was cleared or the app was reinstalled;
- the registration expired or Studio revoked it;
- a different phone tries to use an invite that was already consumed.

The QR is one-use for initial registration. The registered phone does not need
to rescan merely because the original QR later expires.
