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
        reflection.AddRoot(
            "session",
            session,
            new ReflectionRootMetadata("Current Session")
            {
                Description = "Active session view model",
                Category = "view-model",
                Tags = ["debug", "session"]
            },
            root => root
                .AllowWritableMembers("SelectedTab")
                .AllowInvokableMethods("Refresh()"));
    })
    .WithReadWriteToolAccess()
    .Build();
```

Direct object roots use weak references by default. Use `AddStrongRoot(...)` when the root should be retained for the lifetime of the toolsuite.

Write and invoke operations are explicitly allow-listed per root:

- writable members are matched by relative member path such as `Child.Name`
- invokable methods are matched by `Method(Type)` for root methods or `Path#Method(Type)` for nested targets

These tools are intended for local debugging only and may expose or mutate sensitive runtime state.
