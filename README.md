# Ansight SDK

![`branding/logo.png`](branding/logo.png)

Ansight SDK provides cross-platform observability tooling for mobile applications, with SDKs for:

- .NET / MAUI (`src/dotnet`)
- Android (`src/android`)
- iOS (`src/ios`)
- React Native (`src/react-native`)

The Android, iOS, and React Native SDKs are pre-release packages that mirror the
same native runtime, host connection, telemetry, screenshot, touch capture, and
remote-tool protocol used by the .NET SDK.

Current SDK features include:

- live and retained metrics, events, lifecycle, screen-view, memory, FPS, and
  battery telemetry
- saved, bundled, explicit, and remembered-host pairing with automatic reconnect
- live screenshots, host-owned simulator/emulator capture, touch capture,
  session properties, device profiles, and app-provided logs
- guarded native tools for UI, files, file descriptors, preferences, secure
  storage, SQLite, reflection, and framework-specific inspection
- custom remote tools and requestable app artifact providers on every SDK
- .NET MAUI automation, Debug-only annotated feedback, and offline capture,
  export, and team upload workflows
- React component/shadow-tree inspection, React Navigation tracking, and
  JavaScript error capture

All SDKs expose the host auto-probe options used by the .NET runtime. Host
auto-probe remembers previous host connections and retries them while the
runtime is active, so an app can reconnect after the host disappears and later
reappears. The shared options control enable/disable, initial probe delay,
disconnected probe interval, retry delay after a lost session, and the optional
client name used for retry attempts.

> **Important:** Screen capture is not free. Periodic or manual screenshot/JPEG
> capture will result in an FPS drop while frames are being rendered, encoded,
> and transported. Keep it scoped to local development or QA runs, and disable it
> for performance measurements unless visual evidence is required.

## Docs

- [Current Feature Catalog](docs/features.md)
- [Cross-SDK API Parity](docs/sdk-api-parity.md)
- [.NET SDK Guide](src/dotnet/README.md)
- [Android SDK Guide](src/android/README.md)
- [iOS SDK Guide](src/ios/README.md)
- [React Native SDK Guide](src/react-native/README.md)
- [Protocol](docs/protocol.md)

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.
