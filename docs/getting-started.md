# Getting started

This quickstart ends with a development build connected to the local Ansight
host. An account is not required for this workflow.

## 1. Install and check the CLI

On macOS or Linux:

```sh
curl -fsSL https://www.ansight.ai/install.sh | bash
```

On Windows PowerShell:

```powershell
irm https://www.ansight.ai/install.ps1 | iex
```

Open a new terminal, then verify the installation:

```sh
ansight --version
ansight doctor
```

## 2. Initialize a development build

Use the all-in-one package for the first integration. Keep package inclusion,
initialization, enrollment UI, and remote tools behind the app's existing
development or QA build guard.

### Android

Initialize the aggregate Android artifact from the app's `Application`:

```kotlin
if (BuildConfig.DEBUG) {
    Ansight.initializeAndActivateDeveloperMode(
        application = application,
        clientName = "Android App",
    )
}
```

Google Code Scanner owns physical-device camera interaction, so the host app
does not need to request `CAMERA`. The SDK declares the ordinary network
permissions it uses. Kotlin source projects need Kotlin Gradle plugin 1.8 or
newer.

### iOS

Initialize the aggregate `Ansight` Swift package once from app startup:

```swift
#if DEBUG
try AnsightRuntime.shared.initializeAndActivateAnsightSdk()
#endif
```

The SDK-owned physical-device scanner requires `NSCameraUsageDescription`, and
direct LAN access triggers Apple's Local Network privacy control. Ansight does
not request Bluetooth, location, contacts, photos, or Bonjour discovery.

### React Native

```ts
if (__DEV__) {
  await Ansight.initializeAndActivate({
    useNativeAllInOneDefaults: true,
    clientName: "React Native App",
    toolGuard: "readOnly",
  });
}
```

Expo apps use the same `@ansight/react-native` package. Add
`@ansight/react-native` to the app config `plugins` array, then create an Expo
development or EAS build so the native module and iOS permission descriptions
are included. Expo Go and Expo Web are not supported. See the
[React Native SDK guide](../src/react-native/README.md#expo-development-builds).

### Capacitor

```ts
if (isDevelopmentBuild) {
  await Ansight.initializeAndActivate(
    Ansight.createOptionsBuilder()
      .withAnsightDefaults()
      .withReadOnlyToolAccess()
      .withDomTools()
      .withErrorCapture()
      .build(),
  );
}
```

### Flutter

```dart
if (kDebugMode) {
  await Ansight.instance.initializeAndActivate(
    AnsightOptions.developer(
      clientName: 'My Flutter App',
      toolGuard: AnsightToolGuard.readOnly,
    ),
  );
  await AnsightFlutterInstrumentation.instance.install();
}
```

### .NET and MAUI

Initialize the all-in-one SDK once during app startup:

```csharp
#if DEBUG
var options = Options.CreateBuilder()
    .WithAnsightSdk()
    .Build();

Runtime.InitializeAndActivate(options);
#endif
```

For MAUI, use `builder.UseAnsight<App>()` inside the same development-build
guard. The exact startup location varies by platform, but initialize once
rather than from a page or screen.

## 3. Start the host and launch the app

Start the local host and leave it running:

```sh
ansight host run
```

Launch the development build. Simulators, emulators, Mac Catalyst, and desktop
apps register automatically through loopback. There is no pairing file,
generated resource, certificate, signing key, host address, build constant, or
account setup. If the host is unavailable, the SDK retries without failing or
delaying the app.

## 4. Verify the connection

From another terminal, list connected sessions and inspect the enabled tool
catalog:

```sh
ansight session list --connected --json
ansight app tools <session-id> --json
```

## Connect a physical device

A physical device cannot use the host's loopback interface. With the local host
running, issue a one-use QR in another terminal:

```sh
ansight pairing issue --qr
```

Open the SDK's enrollment scanner from a developer-only app surface and scan
the QR:

| Platform | Scanner API |
| --- | --- |
| Android | `Ansight.enrollFromQrCode(activity)` |
| iOS | `AnsightRuntime.shared.connect(.qrCode(...))` |
| React Native | `Ansight.enrollFromQrCode(...)` |
| Capacitor | `Ansight.enrollFromQrCode(...)` |
| Flutter | `Ansight.instance.enrollFromQrCode(...)` |
| .NET | `Runtime.HostConnection.ConnectAsync(HostConnectionRequest.QrCode())` |

The invite is not tied to a pre-registered app. The SDK supplies its runtime
app id, the host registers a random app-installation id, and the SDK saves the
registration in private storage for later reconnects.

For an unattended physical-device test build, the native .NET, Android, and
iOS options expose explicit unattended provisioning. The host runner can inject
a fresh one-use enrollment payload at process launch:
`ANSIGHT_ENROLLMENT_PAYLOAD` on iOS, or the
`ai.ansight.bootstrap.payload` launcher Intent extra on Android. The option is
disabled by default and successful enrollment is remembered privately by the
app installation.

## Network model

Enrollment uses UDP followed by `ws://`. Automatic local registration is
loopback-only. Physical-device enrollment uses a one-use bearer invite over the
local network. No certificate is needed. Traffic is not encrypted or
authenticated against an active network attacker, so use physical-device
connections only on a network you trust.

Android manifest integration is automatic. The iOS client uses
Network.framework directly, so it does not need an ATS clear-text exception.
iOS camera and Local Network privacy descriptions cannot be injected into the
final app by Swift Package Manager, so the app must provide the descriptions
when it invokes those features.

## When a physical device needs a fresh scan

Scan a new QR when:

- this physical installation has never registered;
- app storage was cleared or the app was reinstalled;
- the registration expired or the host revoked it;
- a different phone tries to use an invite that was already consumed.

The QR is claimed by the first scanning app installation. The registered phone
does not need to rescan merely because the original QR later expires.
