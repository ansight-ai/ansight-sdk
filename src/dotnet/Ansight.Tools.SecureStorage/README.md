# Ansight.Tools.SecureStorage

Grouped secure-storage tool registrations for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

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

When using the `Ansight` or `Ansight.Maui` all-in-one packages, configure secure-storage access inside the setup callback:

```csharp
using Ansight;
using Ansight.Tools.SecureStorage;

var options = Options.CreateBuilder()
    .WithAnsightSdk(ansight =>
    {
        ansight.WithSecureStorageTools(secure =>
        {
            secure.WithStorageIdentifier("MyApp");
            secure.AllowKeyPrefix("ansight.secure.");
        });
    })
    .Build();
```

Use the same `WithSecureStorageTools(...)` call inside `UseAnsight<App>(...)` for MAUI. The all-in-one setup skips the default secure-storage registration when the callback registers the suite, so the configured storage identifier and key allow-list are used.

## Restrictions

Secure-storage access is deny-all by default. You must explicitly allow keys with:

- `AllowKey(...)` / `AllowKeys(...)`
- `AllowKeyPrefix(...)` / `AllowKeyPrefixes(...)`

Storage selection is configured at registration time:

- `WithStorageIdentifier(...)` sets both the Android encrypted-preferences name and Apple Keychain service.
- `WithAndroidStore(...)` overrides the Android encrypted-preferences name.
- `WithAppleService(...)` overrides the Apple Keychain service.

These tools are intended for local debugging only and may expose highly sensitive application data.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
