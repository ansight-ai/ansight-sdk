# Ansight iOS

The native iOS runtime plan lives in [IMPLEMENTATION_SPEC.md](IMPLEMENTATION_SPEC.md).

The native harness app lives in `Examples/NativeHarness/`.

## Current capabilities

- pairing document parsing and validation for `ansight.pairing-config.v1` and `ansight.pairing-bootstrap.v1`
- connection-hint application and discovery-hint host fallback for local developer flows
- executable tool registration, tool guard policy, and `tool.query` / `tool.call` protocol handling
- SwiftPM build-time developer pairing generation and bundled-tool enforcement

## SwiftPM developer mode

When building this package through SwiftPM, the `AnsightBuildToolPlugin` runs automatically for the `AnsightKit` target.

Environment variables:

- `ANSIGHT_DEVELOPER_PAIRING_ENABLED=true`
- `ANSIGHT_DEVELOPER_PAIRING_SOURCE_FILE=/absolute/path/to/ansight.json` (optional; defaults to `src/ios/ansight.json` when present)
- `ANSIGHT_ALLOW_MCP_TOOLS=true` to permit bundled `AnsightTool` implementations

With developer pairing enabled, the build tool reads the source pairing config, captures local host metadata when available, and generates an embedded bootstrap document that you can access at runtime through `AnsightDeveloperMode.embeddedPairingJson`.

Without `ANSIGHT_ALLOW_MCP_TOOLS=true`, the build fails when the target source contains concrete `AnsightTool` conformances.

## Current limits

- network transport is still not implemented; `openSession(...)` validates and resolves the pairing document locally, then opens a harness-only local session
- the build-time developer pairing and bundled-tool scan currently ship only through SwiftPM; CocoaPods does not yet have equivalent automation
