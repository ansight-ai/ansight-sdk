using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ansight;

internal static class DotNetSessionProperties
{
    internal const string GroupName = "dotnet";

    internal static SessionCustomProperties Create()
    {
        var properties = new SessionCustomProperties();
        foreach (var property in CreateValues())
        {
            properties.Register(GroupName, property.Key, property.Value);
        }

        return properties;
    }

    internal static SessionCustomProperties CreateEffective(SessionCustomProperties? customProperties)
    {
        var properties = Create();
        properties.MergeFrom(customProperties);
        return properties;
    }

    internal static IReadOnlyDictionary<string, string> CreateValues()
    {
        var assembly = typeof(DotNetSessionProperties).Assembly;
        var sdkVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sdkVersion"] = Normalize(sdkVersion) ?? assembly.GetName().Version?.ToString() ?? "unknown",
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["runtimeVersion"] = Environment.Version.ToString(),
            ["runtimeIdentifier"] = RuntimeInformation.RuntimeIdentifier,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            ["is64BitProcess"] = FormatBoolean(Environment.Is64BitProcess),
            ["jitEnabled"] = FormatBoolean(RuntimeFeature.IsDynamicCodeSupported),
            ["aotEnabled"] = FormatBoolean(!RuntimeFeature.IsDynamicCodeCompiled),
            ["dynamicCodeSupported"] = FormatBoolean(RuntimeFeature.IsDynamicCodeSupported),
            ["dynamicCodeCompiled"] = FormatBoolean(RuntimeFeature.IsDynamicCodeCompiled),
            ["garbageCollector"] = GCSettings.IsServerGC ? "server" : "workstation",
            ["gcLatencyMode"] = GCSettings.LatencyMode.ToString(),
            ["maximumGcGeneration"] = GC.MaxGeneration.ToString(CultureInfo.InvariantCulture)
        };

        var targetFramework = Normalize(AppContext.TargetFrameworkName);
        if (targetFramework is not null)
        {
            properties["targetFramework"] = targetFramework;
        }

        return properties;
    }

    internal static bool TryGetValue(string group, string key, out string value)
    {
        value = string.Empty;
        if (!string.Equals(group.Trim(), GroupName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!CreateValues().TryGetValue(key.Trim(), out var resolvedValue))
        {
            return false;
        }

        value = resolvedValue;
        return true;
    }

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
