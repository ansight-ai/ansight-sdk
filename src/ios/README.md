# Ansight iOS

The native iOS runtime plan lives in [IMPLEMENTATION_SPEC.md](IMPLEMENTATION_SPEC.md).

The native harness app lives in `Examples/NativeHarness/`.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

## Current capabilities

- pairing ticket parsing and validation for `ansight.pairing-ticket.v1`
- discovery-hint host resolution for local developer flows
- executable tool registration, tool guard policy, and `tool.query` / `tool.call` protocol handling
- SwiftPM build-time developer pairing generation and bundled-tool enforcement

## SwiftPM developer mode

When building this package through SwiftPM, the `AnsightBuildToolPlugin` runs automatically for the `AnsightKit` target.

Environment variables:

- `ANSIGHT_DEVELOPER_PAIRING_ENABLED=true`
- `ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE=/absolute/path/to/ansight.json` (optional; defaults to `src/ios/ansight.json` when present)
- `ANSIGHT_ALLOW_REMOTE_TOOLS=true` to permit bundled `AnsightTool` implementations

With developer pairing enabled, the build tool reads the source pairing config, captures local host metadata when available, and generates an embedded pairing ticket that you can access at runtime through `AnsightDeveloperMode.embeddedPairingJson`.

Without `ANSIGHT_ALLOW_REMOTE_TOOLS=true`, the build fails when the target source contains concrete `AnsightTool` conformances.

## Current limits

- network transport is still not implemented; `openSession(...)` validates and resolves the pairing ticket locally, then opens a harness-only local session
- the build-time developer pairing and bundled-tool scan currently ship only through SwiftPM; CocoaPods does not yet have equivalent automation
