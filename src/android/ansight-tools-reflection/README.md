# ansight-tools-reflection-android

Android live-object reflection tools.

## Tools

- `reflect.list_roots`
- `reflect.inspect_object`
- `reflect.describe_type`
- `reflect.set_member_value`
- `reflect.invoke_method`

List, inspect, and describe are read-scoped. Setting member values and invoking
methods are write-scoped and require `AnsightToolGuard.ReadWrite` or
`FullAccess`.

## Usage

```kotlin
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.tools.reflection.AndroidReflectionRootRegistry
import ai.ansight.tools.reflection.AndroidReflectionTools

val registration = AndroidReflectionRootRegistry.register(
    id = "session",
    value = sessionViewModel,
    displayName = "Session View Model",
)

val options = AnsightOptions(
    initialTools = AndroidReflectionTools.create(),
    toolGuard = AnsightToolGuard.ReadOnly,
)
```

Reflection roots are the access boundary. Register only objects that are safe to
inspect or mutate, and close the returned registration when the root should no
longer be exposed.
