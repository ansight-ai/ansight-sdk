# Ansight SDK

![`branding/logo.png`](branding/logo.png)

Ansight SDK provides cross-platform observability tooling for mobile applications, with SDKs for:

- .NET / MAUI (`src/dotnet`)
- Android (`src/android`)
- iOS (`src/ios`)
- React Native (`src/react-native`)
- Capacitor (`src/capacitor`)
- Flutter (`src/flutter`)

The Android, iOS, React Native, Capacitor, and Flutter SDKs are pre-release
packages that mirror the same native runtime, host connection, telemetry,
screenshot, touch capture, and remote-tool protocol used by the .NET SDK.

Current SDK features include:

- live and retained metrics, events, lifecycle, screen-view, memory, FPS, and
  battery telemetry
- automatic local-host registration for simulators, emulators, and desktop
  apps, plus one-scan enrollment for physical devices
- live screenshots, host-owned simulator/emulator capture, touch capture,
  session properties, device profiles, and app-provided logs
- HTTP request capture with app-configurable and
  mandatory native sanitization; request and response bodies are excluded
- guarded native tools for UI, files, file descriptors, preferences, secure
  storage, SQLite, reflection, and framework-specific inspection
- custom remote tools and requestable app artifact providers on every SDK
- .NET MAUI automation, Debug-only annotated feedback, and offline capture,
  export, and team upload workflows
- React component/shadow-tree inspection, React Navigation tracking, and
  JavaScript error capture
- Capacitor WebView DOM inspection and actions, route/lifecycle tracking,
  JavaScript tools, artifacts, and error capture
- Flutter widget-tree inspection, navigation and lifecycle tracking, Dart
  tools and artifacts, frame timing, and framework error capture

All SDKs start the same runtime connection loop when activated. Start the local
host with `ansight host run`; no account is required. A simulator, emulator,
Mac Catalyst app, or desktop app registers through loopback without a QR or
build-time configuration. For a physical device, run
`ansight pairing issue --qr` and scan the one-use QR from the SDK's
developer-only enrollment UI. The app then reconnects from private
registration state. If the host is unavailable, the SDK retries without
affecting the app.

> **Important:** Screen capture is not free. Periodic or manual screenshot/JPEG
> capture will result in an FPS drop while frames are being rendered, encoded,
> and transported. Keep it scoped to local development or QA runs, and disable it
> for performance measurements unless visual evidence is required.

## Docs

- [Getting started](docs/getting-started.md)
- [Current Feature Catalog](docs/features.md)
- [Cross-SDK API Parity](docs/sdk-api-parity.md)
- [.NET SDK Guide](src/dotnet/README.md)
- [Android SDK Guide](src/android/README.md)
- [iOS SDK Guide](src/ios/README.md)
- [React Native SDK Guide](src/react-native/README.md)
- [Capacitor SDK Guide](src/capacitor/README.md)
- [Capacitor Test Corpus](test-apps/README.md)
- [Flutter SDK Guide](src/flutter/README.md)
- [Flutter Test Corpus](src/flutter/validation/flutter-corpus-results.md)
- [Protocol](docs/protocol.md)

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.
