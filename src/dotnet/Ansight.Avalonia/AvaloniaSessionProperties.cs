using System.Reflection;
using Avalonia;

namespace Ansight.Avalonia;

internal static class AvaloniaSessionProperties
{
    internal const string GroupName = "avalonia";

    internal static IReadOnlyDictionary<string, string> CreateValues(AppBuilder builder, Type applicationType)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["framework"] = "Avalonia",
            ["version"] = GetVersion(typeof(global::Avalonia.Application).Assembly),
            ["sdkVersion"] = GetVersion(typeof(AvaloniaSessionProperties).Assembly),
            ["applicationType"] = applicationType.FullName ?? applicationType.Name
        };

        AddIfAvailable(properties, "applicationLifetime", builder.Instance?.ApplicationLifetime?.GetType().FullName);
        AddIfAvailable(properties, "windowingSubsystem", builder.WindowingSubsystemName);
        AddIfAvailable(properties, "renderingSubsystem", builder.RenderingSubsystemName);

        return properties;
    }

    private static string GetVersion(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return string.IsNullOrWhiteSpace(version)
            ? assembly.GetName().Version?.ToString() ?? "unknown"
            : version;
    }

    private static void AddIfAvailable(Dictionary<string, string> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[key] = value;
        }
    }
}
