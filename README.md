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

## Docs

- [Cross-SDK API Parity](docs/sdk-api-parity.md)
- [.NET SDK Guide](src/dotnet/docs.md)
- [Android SDK Guide](src/android/README.md)
- [iOS SDK Guide](src/ios/README.md)
- [React Native SDK Guide](src/react-native/README.md)
- [SDK Publishing](docs/sdk-publishing.md)
- [Remote Tool Protocol](docs/protocol/tools.md)

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.
