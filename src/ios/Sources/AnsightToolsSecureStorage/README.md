# AnsightToolsSecureStorage

Allow-listed Keychain tools for iOS apps.

## Tools

- `secure.get_value`
- `secure.set_value`
- `secure.remove_key`

Get requires `.readOnly`. Set requires `.readWrite`; remove requires
`.fullAccess`.

## Usage

```swift
import AnsightCore
import AnsightToolsSecureStorage

let options = AnsightSecureStorageToolsOptions.createBuilder()
    .withStorageIdentifier("com.example.app")
    .allowKey("debug_token")
    .allowKeyPrefix("ansight.")
    .build()

try AnsightRuntime.shared.registerSecureStorageTools(options: options)
```

Keys are denied by default. Add explicit keys or prefixes before exposing this
tool suite.
