# AnsightToolsPreferences

`UserDefaults` remote tools for iOS apps.

## Tools

- `prefs.list_keys`
- `prefs.get_value`
- `prefs.set_value`
- `prefs.remove_key`

Read tools are available with `.readOnly`. Set requires `.readWrite`; remove
requires `.fullAccess`.

## Usage

```swift
import AnsightCore
import AnsightToolsPreferences

let options = AnsightPreferencesToolOptionsBuilder()
    .withDefaultStore(Bundle.main.bundleIdentifier)
    .allowKeyPrefix("debug.")
    .build()

try AnsightRuntime.shared.registerPreferencesTools(options: options)
```

Use `allowedStores`, `allowedKeys`, and `allowedKeyPrefixes` to limit what a
connected client can inspect or mutate.
