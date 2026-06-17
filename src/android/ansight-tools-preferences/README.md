# ansight-tools-preferences-android

Android `SharedPreferences` remote tools.

## Tools

- `prefs.list_keys`
- `prefs.get_value`
- `prefs.set_value`
- `prefs.remove_key`

Read tools are available with `AnsightToolGuard.ReadOnly`. Set requires
`ReadWrite`; remove requires `FullAccess`.

## Usage

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.tools.preferences.AndroidPreferencesTools

val options = AnsightOptions(
    initialTools = AndroidPreferencesTools.create(),
    toolGuard = AnsightToolGuard.ReadWrite,
)
```

The `name` argument selects a `SharedPreferences` file. If omitted, the package
uses `<applicationId>_preferences`.
