# ansight-tools-securestorage-android

Allow-listed Android secure storage tools backed by the SDK secure-storage
options.

## Tools

- `secure.get_value`
- `secure.set_value`
- `secure.remove_key`

Get requires `AnsightToolGuard.ReadOnly`. Set requires `ReadWrite`; remove
requires `FullAccess`.

## Usage

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightSecureStorageOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.tools.securestorage.AndroidSecureStorageTools

val options = AnsightOptions(
    initialTools = AndroidSecureStorageTools.create(),
    toolGuard = AnsightToolGuard.ReadWrite,
    secureStorage = AnsightSecureStorageOptions(
        preferencesName = "com.example.secure",
        allowedKeys = setOf("debug_token"),
        allowedPrefixes = setOf("ansight."),
    ),
)
```

Keys are denied by default. Add explicit keys or prefixes before exposing this
tool suite.
