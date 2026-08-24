# Ansight.Tools.Preferences

Grouped shared-preferences and user-defaults tool registrations for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

Registered tools:

- `prefs.list_keys`
- `prefs.get_value`
- `prefs.set_value`
- `prefs.remove_key`

## Usage

```csharp
using Ansight;
using Ansight.Tools.Preferences;

var options = Options.CreateBuilder()
    .WithPreferencesTools(preferences =>
    {
        preferences.AllowKeyPrefix("ansight.");
    })
    .WithReadWriteToolAccess()
    .Build();
```

`prefs.remove_key` uses `ToolPolicy.Critical`. Use `WithAllToolAccess()` or a
custom critical-enabled `ToolGuard` to execute it.

## Restrictions

Preferences tools can be constrained at registration time:

- `AllowStore(...)` / `AllowStores(...)`
- `AllowKey(...)` / `AllowKeys(...)`
- `AllowKeyPrefix(...)` / `AllowKeyPrefixes(...)`
- `WithDefaultStore(...)`

When a value is not a plain string, the tool returns a string representation and a `valueType`. `string_array` values are represented as JSON arrays encoded in the `value` field.

These tools are intended for local debugging only and may expose sensitive application data.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
