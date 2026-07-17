namespace Ansight.Annotations;

using System.Diagnostics;
using System.Reflection;

internal static class AnnotationBuildPolicy
{
    private const string MetadataKey = "Ansight.Annotations.DebugBuild";

    internal static bool IsDebugBuild(Assembly registrationAssembly)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (TryReadMetadata(entryAssembly, out var entryDebugBuild))
        {
            return entryDebugBuild;
        }

        if (TryReadMetadata(registrationAssembly, out var registrationDebugBuild))
        {
            return registrationDebugBuild;
        }

        if (entryAssembly is not null && !IsTestHost(entryAssembly))
        {
            return HasDebuggableCode(entryAssembly);
        }

        return HasDebuggableCode(registrationAssembly);
    }

    private static bool TryReadMetadata(Assembly? assembly, out bool isDebugBuild)
    {
        var value = assembly?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .LastOrDefault(attribute => string.Equals(attribute.Key, MetadataKey, StringComparison.Ordinal))?
            .Value;
        return bool.TryParse(value, out isDebugBuild);
    }

    private static bool HasDebuggableCode(Assembly assembly)
    {
        var attribute = assembly.GetCustomAttribute<DebuggableAttribute>();
        return attribute is not null &&
               (attribute.IsJITTrackingEnabled ||
                attribute.DebuggingFlags.HasFlag(DebuggableAttribute.DebuggingModes.DisableOptimizations) ||
                attribute.DebuggingFlags.HasFlag(DebuggableAttribute.DebuggingModes.EnableEditAndContinue));
    }

    private static bool IsTestHost(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return string.Equals(name, "testhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "vstest.console", StringComparison.OrdinalIgnoreCase);
    }
}
