# Ansight.Profiling.DotNet

Opt-in application support for startup profiles captured by Ansight. The package
configures a profiling artifact's .NET diagnostic endpoint and provides an
explicit application-ready signal. It does not build, install, launch, or
profile the application.

Reference the package normally, then enable profiling only in a dedicated build
configuration. Replace `SDK_VERSION` with the published Ansight SDK version you
are using:

```xml
<ItemGroup>
  <PackageReference Include="Ansight.Profiling.DotNet" Version="SDK_VERSION" />
</ItemGroup>

<PropertyGroup Condition="'$(Configuration)' == 'Profile'">
  <AnsightProfilingEnabled>true</AnsightProfilingEnabled>
  <AnsightProfilingTarget>android-emulator</AnsightProfilingTarget>
</PropertyGroup>
```

Supported targets are `android-emulator`, `android-device`, `ios-simulator`,
and `ios-device`. The package embeds `ansight/dotnet-profiling.json` in the
resulting app so Ansight can verify that the artifact matches the selected
device before launch.

Call `ApplicationReady` at the point that best represents a usable application:

```csharp
using Ansight.Profiling;

AnsightProfiler.ApplicationReady();
```

The call is idempotent. It emits `StartupComplete` once from the
`Ansight-DotNet-Startup` EventSource when an EventPipe listener is active.

Profiling diagnostics expose sensitive runtime information. Never distribute a
profiling-enabled artifact to production or an app store.
