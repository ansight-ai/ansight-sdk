# .NET Storage MCP Tools Plan

## Summary

Add two new Ansight .NET MCP tool packages for app storage inspection and mutation:

- `Ansight.Tools.Preferences`
- `Ansight.Tools.SecureStorage`

These packages should expose a cross-platform tool surface while using native platform implementations under the hood:

- Android:
  - preferences via `SharedPreferences`
  - secure storage via Android Keystore-backed storage
- iOS:
  - preferences via `NSUserDefaults`
  - secure storage via Keychain
- Mac Catalyst:
  - preferences via `NSUserDefaults`
  - secure storage via Keychain

This work should follow the same packaging and fluent registration pattern already used by:

- `Ansight.Tools.FileSystem`
- `Ansight.Tools.Database`
- `Ansight.Tools.VisualTree`

## Goals

- Add native Android, iOS, and Mac Catalyst storage MCP tool SDKs as separate NuGet packages.
- Keep the public tool contracts consistent across platforms.
- Support both read and write operations.
- Preserve the current Ansight security posture:
  - MCP tools remain explicitly opt-in
  - Debug-only usage is strongly preferred
  - high-risk storage access stays gated by `ToolGuard`

## Non-Goals

- Do not build these as MAUI-only wrappers.
- Do not rely on MAUI `Preferences` or MAUI `SecureStorage` as the primary implementation layer.
- Do not expose secure-storage key enumeration in v1 unless native parity and security constraints are clearly defined.

## Proposed Packages

### 1. `Ansight.Tools.Preferences`

Purpose: inspect and mutate app shared preferences / user defaults.

Proposed fluent registration:

```csharp
using Ansight;
using Ansight.Tools.Preferences;

var options = Options.CreateBuilder()
    .WithPreferencesTools()
    .WithToolGuard(new ToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: [ToolScope.Read, ToolScope.Write]))
    .Build();
```

Proposed tool set:

- `prefs.list_keys`
- `prefs.get_value`
- `prefs.set_value`
- optional: `prefs.remove_key`

### 2. `Ansight.Tools.SecureStorage`

Purpose: inspect and mutate app secure storage using native secure stores.

Proposed fluent registration:

```csharp
using Ansight;
using Ansight.Tools.SecureStorage;

var options = Options.CreateBuilder()
    .WithSecureStorageTools(secure =>
    {
        secure.AllowKeys("session_token", "refresh_token");
    })
    .WithToolGuard(new ToolGuard(
        discoveryEnabled: true,
        executionEnabled: true,
        allowedScopes: [ToolScope.Read, ToolScope.Write]))
    .Build();
```

Proposed tool set:

- `secure.get_value`
- `secure.set_value`
- optional: `secure.remove_key`

Not recommended for v1:

- `secure.list_keys`

Reason: Android/Apple secure-store enumeration behavior is not a clean cross-platform guarantee and exposes more risk than value.

## Native Implementation Strategy

### Preferences

Use partial or platform-split support files:

- `PreferencesSupport.Android.cs`
- `PreferencesSupport.Apple.cs`
- `PreferencesSupport.Default.cs`

Platform behavior:

- Android:
  - read/write named `SharedPreferences`
  - support a default store name when not specified
- Apple (`iOS` + `Mac Catalyst`):
  - read/write `NSUserDefaults`
  - support standard defaults first
  - optional suite-name support if needed later

Returned values should be normalized into a common JSON shape:

- `key`
- `value`
- `valueType`
- `store`
- `capturedAtUtc`

### Secure Storage

Use platform-split support files:

- `SecureStorageSupport.Android.cs`
- `SecureStorageSupport.Apple.cs`
- `SecureStorageSupport.Default.cs`

Platform behavior:

- Android:
  - use a direct Keystore-backed implementation
  - avoid taking a new dependency unless native implementation proves too costly
- Apple (`iOS` + `Mac Catalyst`):
  - use Keychain APIs directly

Returned values should be normalized into a common JSON shape:

- `key`
- `value`
- `exists`
- `store` or `service` when applicable
- `capturedAtUtc`

## Security Model

This work is materially more sensitive than the current read-only packages.

### Tool scopes

- read tools: `ToolScope.Read`
- write tools: `ToolScope.Write`
- delete/remove tools: `ToolScope.Delete`

### Guard requirements

Current convenience helpers only cover:

- read-only access
- full access

That means the SDK should either:

1. add `WithReadWriteToolAccess()`, or
2. document `WithToolGuard(...)` as the expected configuration for storage tools

Preferred option: add `WithReadWriteToolAccess()` in the base SDK so apps do not need to hand-roll the most common safe write configuration.

### Restrictions

Storage tools should support explicit restrictions at registration time.

Preferences restrictions:

- allowed store names
- allowed key names
- allowed key prefixes

Secure storage restrictions:

- explicit allow-list of keys
- optional allowed key prefixes if needed

Secure storage should default to deny-all unless keys are explicitly allowed.

## Package Structure

Add new projects under `src/dotnet`:

- `Ansight.Tools.Preferences/Ansight.Tools.Preferences.csproj`
- `Ansight.Tools.SecureStorage/Ansight.Tools.SecureStorage.csproj`

Add both to:

- `src/dotnet/Ansight.Sdk.sln`

Each package should mirror the existing pattern:

- tool schema definitions
- tool classes
- support/helper classes
- options builder extensions
- README
- packable multi-targeted project

Target frameworks should match the existing tool packages:

- `net9.0`
- `net9.0-android`
- `net9.0-ios`
- `net9.0-maccatalyst`

`net9.0` should return `platform_unsupported` through default support files so unit tests can still compile and run on host.

## Tool Surface Details

### Preferences

#### `prefs.list_keys`

Arguments:

- `store` optional
- `prefix` optional
- `maxResults` optional

Result:

- `store`
- `keys`
- `truncated`
- `capturedAtUtc`

#### `prefs.get_value`

Arguments:

- `key` required
- `store` optional

Result:

- `store`
- `key`
- `exists`
- `value`
- `valueType`
- `capturedAtUtc`

#### `prefs.set_value`

Arguments:

- `key` required
- `value` required
- `valueType` required
- `store` optional

Result:

- `store`
- `key`
- `valueType`
- `updated`
- `capturedAtUtc`

#### `prefs.remove_key` optional

Arguments:

- `key` required
- `store` optional

Result:

- `store`
- `key`
- `removed`
- `capturedAtUtc`

### Secure Storage

#### `secure.get_value`

Arguments:

- `key` required

Result:

- `key`
- `exists`
- `value`
- `capturedAtUtc`

#### `secure.set_value`

Arguments:

- `key` required
- `value` required

Result:

- `key`
- `updated`
- `capturedAtUtc`

#### `secure.remove_key` optional

Arguments:

- `key` required

Result:

- `key`
- `removed`
- `capturedAtUtc`

## Implementation Phases

### Phase 1: Base SDK guard ergonomics

- Add `WithReadWriteToolAccess()` to `Options.OptionsBuilder`, or explicitly decide not to and document custom `ToolGuard` usage.
- Add unit tests for `Read + Write` scope behavior.

### Phase 2: Preferences package

- Create project and README.
- Add schemas and tool definitions.
- Implement Android `SharedPreferences` support.
- Implement Apple `NSUserDefaults` support for `iOS` and `Mac Catalyst`.
- Add host fallback implementation.
- Add unit tests for:
  - tool registration
  - schema validity
  - guard behavior
  - allowed-key / allowed-store filtering
  - type normalization

### Phase 3: Secure storage package

- Create project and README.
- Add schemas and tool definitions.
- Implement Android Keystore-backed support.
- Implement Apple Keychain support for `iOS` and `Mac Catalyst`.
- Add host fallback implementation.
- Add options for explicit key allow-listing.
- Add unit tests for:
  - tool registration
  - denied key access
  - missing key behavior
  - read/write/remove behavior through abstraction seams

### Phase 4: Harness integration

Use the MAUI test harness only as a validation host.

- Add project references to the new packages in Debug only.
- Set `AnsightAllowRemoteTools=true` in Debug only.
- Register the new tool packages in `MauiProgram.cs`.
- Seed the harness with known test values for preferences and secure storage.
- Add simple in-app controls or startup seeding to validate native behavior.

### Phase 5: Apple metadata updates

- Enable the `NSUserDefaults` privacy manifest entry in the MAUI harness for Apple targets.
- Confirm whether any additional Keychain-related entitlements or configuration are needed for the chosen secure-storage implementation on `iOS` and `Mac Catalyst`.

### Phase 6: Docs

Update:

- root .NET docs
- package READMEs
- any examples that demonstrate MCP tool registration

Documentation should clearly state:

- tools are for local debugging only
- apps must opt into MCP tools explicitly
- storage tools may expose sensitive user/application data

## Testing Plan

### Unit tests

Add tests similar in shape to the existing tool package tests.

Cover:

- package registration via fluent extensions
- schema validation
- success and failure payloads
- scope enforcement
- restriction enforcement
- host fallback behavior

### Device validation

Validate on:

- Android device or emulator
- iOS simulator/device
- Mac Catalyst app host

Scenarios:

- read existing preference
- write preference and read back
- remove preference if supported
- write secure value and read back
- remove secure value if supported
- verify denied keys are rejected

## Risks

- Secure storage parity between Android and Apple platforms is weaker than preferences parity.
- Key enumeration in secure storage is likely to become a design and security problem.
- Preferences value typing differs by platform and must be normalized carefully.
- Any write-capable tool increases the importance of precise guard configuration.
- Build-time MCP enforcement already exists, but harness and sample wiring must stay Debug-only.

## Acceptance Criteria

- Two new NuGet-packable tool packages exist and build successfully.
- Preferences tools use native Android, iOS, and Mac Catalyst implementations.
- Secure storage tools use native Android, iOS, and Mac Catalyst implementations.
- Host builds compile with default fallback implementations.
- Tool registration matches existing Ansight package conventions.
- Storage writes require explicit non-read-only guard configuration.
- The MAUI harness can demonstrate end-to-end storage reads and writes in Debug builds.
- Docs clearly describe registration, guard requirements, and security constraints.

## Open Decision

One implementation choice remains:

- Android secure storage should be implemented directly against native crypto/Keystore APIs unless integration cost becomes unreasonable.

Current recommendation:

- start with a direct native implementation to keep dependencies and runtime behavior under Ansight control
- only introduce a dependency if native implementation complexity materially slows delivery or weakens reliability
