# Ansight.Tools.Preferences

Grouped shared-preferences and user-defaults tool registrations for the Ansight .NET SDK.

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

`prefs.remove_key` is delete-scoped. Use `WithAllToolAccess()` or a custom `ToolGuard` if you want delete operations to execute.

## Restrictions

Preferences tools can be constrained at registration time:

- `AllowStore(...)` / `AllowStores(...)`
- `AllowKey(...)` / `AllowKeys(...)`
- `AllowKeyPrefix(...)` / `AllowKeyPrefixes(...)`
- `WithDefaultStore(...)`

When a value is not a plain string, the tool returns a string representation and a `valueType`. `string_array` values are represented as JSON arrays encoded in the `value` field.

These tools are intended for local debugging only and may expose sensitive application data.
