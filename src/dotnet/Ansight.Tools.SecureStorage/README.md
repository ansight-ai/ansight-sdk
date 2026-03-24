# Ansight.Tools.SecureStorage

Grouped secure-storage tool registrations for the Ansight .NET SDK.

Registered tools:

- `secure.get_value`
- `secure.set_value`
- `secure.remove_key`

## Usage

```csharp
using Ansight;
using Ansight.Tools.SecureStorage;

var options = Options.CreateBuilder()
    .WithSecureStorageTools(secure =>
    {
        secure.WithStorageIdentifier("MyApp");
        secure.AllowKeys("session_token", "refresh_token");
    })
    .WithReadWriteToolAccess()
    .Build();
```

`secure.remove_key` is delete-scoped. Use `WithAllToolAccess()` or a custom `ToolGuard` if you want delete operations to execute.

## Restrictions

Secure-storage access is deny-all by default. You must explicitly allow keys with:

- `AllowKey(...)` / `AllowKeys(...)`
- `AllowKeyPrefix(...)` / `AllowKeyPrefixes(...)`

Storage selection is configured at registration time:

- `WithStorageIdentifier(...)` sets both the Android encrypted-preferences name and Apple Keychain service.
- `WithAndroidStore(...)` overrides the Android encrypted-preferences name.
- `WithAppleService(...)` overrides the Apple Keychain service.

These tools are intended for local debugging only and may expose highly sensitive application data.
