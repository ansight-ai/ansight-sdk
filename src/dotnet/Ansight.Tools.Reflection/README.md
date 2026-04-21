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

var options = Options.CreateBuilder()
    .WithReflectionTools(reflection =>
    {
        reflection.WithDefaultMemberVisibility(ReflectionMemberVisibility.PublicOnly);
    })
    .WithReadWriteToolAccess()
    .Build();

using var sessionRoot = ReflectionRootRegistry.Register(
    "session",
    session,
    new ReflectionRootMetadata("Current Session")
    {
        Description = "Active session view model",
        Hints = ["debug", "session"]
    });

using var detailRoot = ReflectionRootRegistry.Register(
    "details",
    () => session.CurrentDetails,
    new ReflectionRootMetadata("Current Details"));

detailRoot.Deregister();
```

Registering a root grants access to visible members and instance methods reachable from that root. The tools use stateless paths from registered roots; there are no per-member allow-list APIs in the current simplified surface. Choose the roots you expose carefully, and choose an appropriate tool guard.

Direct object roots use weak references by default when registered with `Register(...)`. Pass `ReferenceType.Strong` when the registry should retain the root for the lifetime of the toolsuite. Register a `Func<object?>` getter when the exposed root can change over time, such as the current view model or selected document; the root is reported as unavailable while the getter returns `null`. Runtime registration returns a `ReflectionRootRegistrationHandle`; dispose it or call `Deregister()` to remove that specific registration, or call `ReflectionRootRegistry.Deregister(id)` to remove the current root by identifier. Metadata, including `Description` and `Hints`, is supplied through the `ReflectionRootMetadata` argument.

Recursive traversal is open by default. Use `WithAssemblyTraversalMode(ReflectionAssemblyTraversalMode.AllowListedOnly)` and `WithNamespaceTraversalMode(ReflectionNamespaceTraversalMode.AllowListedOnly)` with `AllowAssembly(...)` / `AllowNamespacePrefix(...)` only when you need to restrict expansion to selected assemblies or namespaces.

`WithReadOnlyToolAccess()` exposes `reflect.list_roots`, `reflect.inspect_object`, and `reflect.describe_type`. `reflect.set_member_value` and `reflect.invoke_method` are write-scoped and require `WithReadWriteToolAccess()` or a custom `ToolGuard`.

These tools are intended for local debugging only and may expose or mutate sensitive runtime state.
