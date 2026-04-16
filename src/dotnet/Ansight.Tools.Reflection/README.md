# Ansight.Tools.Reflection

Grouped live-object reflection tools for the Ansight .NET SDK.

Registered tools:

- `reflect.list_roots`
- `reflect.inspect_object`
- `reflect.describe_type`
- `reflect.set_member_value`
- `reflect.invoke_method`

## Usage

```csharp
using Ansight;
using Ansight.Tools.Reflection;

var session = new DebugSessionViewModel();

var reflectionOptions = ReflectionToolsOptions.CreateBuilder()
    .WithDefaultMemberVisibility(ReflectionMemberVisibility.PublicOnly)
    .Build();

var options = Options.CreateBuilder()
    .WithReflectionTools(reflectionOptions)
    .WithReadWriteToolAccess()
    .Build();

using var sessionRoot = ReflectionRootRegistry.Register(
    "session",
    session,
    new ReflectionRootMetadata("Current Session")
    {
        Description = "Active session view model",
        Hints = ["debug", "session"],
        ContainsSensitiveData = true
    });

using var detailRoot = ReflectionRootRegistry.Register(
    "details",
    session.CurrentDetails,
    new ReflectionRootMetadata("Current Details"),
    ReferenceType.Strong);

detailRoot.Deregister();
```

Registering a root grants access to visible members and instance methods reachable from that root. Writes still require the target field or property to be writable, and non-public members are only visible when configured with `WithDefaultMemberVisibility(...)`.

Direct object roots use weak references by default when registered with `Register(...)`. Pass `ReferenceType.Strong` when the root should be retained for the lifetime of the toolsuite. Runtime registration returns a `ReflectionRootRegistrationHandle`; dispose it or call `Deregister()` to remove that specific registration, or call `ReflectionRootRegistry.Deregister(id)` to remove the current root by identifier. Metadata, including `Hints`, is supplied through the `ReflectionRootMetadata` argument.

Recursive traversal is open by default. Use `WithAssemblyTraversalMode(ReflectionAssemblyTraversalMode.AllowListedOnly)` and `WithNamespaceTraversalMode(ReflectionNamespaceTraversalMode.AllowListedOnly)` with `AllowAssembly(...)` / `AllowNamespacePrefix(...)` only when you need to restrict expansion to selected assemblies or namespaces.

These tools are intended for local debugging only and may expose or mutate sensitive runtime state.
