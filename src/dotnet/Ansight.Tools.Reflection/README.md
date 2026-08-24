# Ansight.Tools.Reflection

Grouped live-object reflection tools for the Ansight .NET SDK.

## License

The Ansight SDK is source-available software under the
[Ansight SDK Source-Available License](https://github.com/ansight-ai/ansight-sdk/blob/main/LICENSE).
It is not open-source software. Production use is licensed only for use with
Ansight Services.

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

`reflect.list_roots` includes a `hostRuntime` descriptor on each root. .NET
roots report `kind: "dotnet"` so Studio and agent bridges can distinguish
CLR-hosted roots from roots hosted by other SDK runtimes such as JVM, Swift, or
future React Native JavaScript reflection roots.

Recursive traversal is open by default. Use `WithAssemblyTraversalMode(ReflectionAssemblyTraversalMode.AllowListedOnly)` and `WithNamespaceTraversalMode(ReflectionNamespaceTraversalMode.AllowListedOnly)` with `AllowAssembly(...)` / `AllowNamespacePrefix(...)` only when you need to restrict expansion to selected assemblies or namespaces.

`reflect.list_roots` and `reflect.describe_type` use `ToolPolicy.Read`.
`reflect.inspect_object`, `reflect.set_member_value`, and
`reflect.invoke_method` use `ToolPolicy.Critical` because they can disclose
sensitive state or execute arbitrary app code; they require
`WithAllToolAccess()` or a custom critical-enabled `ToolGuard`.

These tools are intended for local debugging only and may expose or mutate sensitive runtime state.

## Build-time remote tool policy

Projects that reference this package are covered by `AnsightRemoteToolsPolicy`. The default `AllowedWithWarnings` policy logs detected tool type and assembly details and emits a build warning when remote tools are included. Because this package contains remote tools, `Disallowed` only succeeds when the package is omitted from that build, for example with Debug-only package references. Use `Allowed` to bypass remote tool scanning and warnings. Set `AnsightLogRemoteTools=false` to suppress the detected-tool list.
