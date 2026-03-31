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
        reflection.WithAssemblyTraversalMode(ReflectionAssemblyTraversalMode.AllowAll);
        reflection.WithNamespaceTraversalMode(ReflectionNamespaceTraversalMode.AllowAll);
        reflection.AddRoot(
            "session",
            session,
            new ReflectionRootMetadata("Current Session")
            {
                Description = "Active session view model",
                Hints = ["debug", "session"],
                ContainsSensitiveData = true
            },
            root => root
                .AllowWritableMembers("SelectedTab")
                .AllowAllWritableMembersOn<DebugSessionViewModel>()
                .AllowInvokableMethods("Refresh()")
                .AllowAllInvokableMethodsOn<DebugSessionViewModel>());
    })
    .WithReadWriteToolAccess()
    .Build();
```

Direct object roots use weak references by default. Use `AddStrongRoot(...)` when the root should be retained for the lifetime of the toolsuite.

Recursive traversal is allow-listed by default. Use `WithAssemblyTraversalMode(...)` and `WithNamespaceTraversalMode(...)` to switch either boundary to `AllowAll`, or keep the default `AllowListedOnly` mode and add entries with `AllowAssembly(...)` / `AllowNamespacePrefix(...)`.

Write and invoke operations are explicitly allow-listed per root:

- writable members are matched by relative member path such as `Child.Name`
- invokable methods are matched by `Method(Type)` for root methods or `Path#Method(Type)` for nested targets
- `AllowAllWritableMembersOn<T>()` / `AllowAllInvokableMethodsOn<T>()` enable all writable members or invokable methods for reachable objects assignable to a given type
- `AllowAllWritableMembers()` / `AllowAllInvokableMethods()` enable those capabilities for all reachable objects under the root

These tools are intended for local debugging only and may expose or mutate sensitive runtime state.
