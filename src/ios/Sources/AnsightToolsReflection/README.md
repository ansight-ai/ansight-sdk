# AnsightToolsReflection

iOS live-object reflection tools.

## Tools

- `reflect.list_roots`
- `reflect.inspect_object`
- `reflect.describe_type`
- `reflect.set_member_value`
- `reflect.invoke_method`

List, inspect, and describe are read-scoped. Setting member values and invoking
methods are write-scoped and require a tool guard that permits write tools.

Swift does not expose arbitrary runtime property writes or method invocation in
the same way as .NET and the JVM. Inspection uses `Mirror`; writes and method
invocation are available only for roots that opt in through
`AnsightReflectionMutableRoot` and `AnsightReflectionInvokableRoot`.

## Usage

```swift
import Ansight

final class SessionInspector: AnsightReflectionMutableRoot, AnsightReflectionInvokableRoot {
    var title = "Checkout"

    func setReflectionValue(path: String, value: JSONValue) throws -> JSONValue? {
        guard path == "title", case .string(let title) = value else {
            return nil
        }
        self.title = title
        return .string(title)
    }

    func invokeReflectionMethod(targetPath: String?, method: String, arguments: [JSONValue]) throws -> JSONValue? {
        guard method == "reset" else {
            return nil
        }
        title = "Checkout"
        return .string(title)
    }
}

let inspector = SessionInspector()
try AnsightReflectionRootRegistry.register(
    id: "session",
    target: inspector,
    displayName: "Session Inspector",
    referenceType: .strong
)
```

Reflection roots are the access boundary. Register only objects that are safe to
inspect or mutate, and deregister the returned handle when the root should no
longer be exposed.
