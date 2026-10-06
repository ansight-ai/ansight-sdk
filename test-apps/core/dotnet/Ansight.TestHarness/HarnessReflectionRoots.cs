using Ansight.Tools.Reflection;

namespace Ansight.TestHarness;

internal static class HarnessReflectionRoots
{
    private static ReflectionRootRegistrationHandle? runtimeRootHandle;
    private static ReflectionRootRegistrationHandle? configurationRootHandle;

    public static void Register()
    {
        runtimeRootHandle?.Dispose();
        configurationRootHandle?.Dispose();

        runtimeRootHandle = ReflectionRootRegistry.Register(
            "harness.runtime",
            () => new HarnessRuntimeReflectionRoot(),
            new ReflectionRootMetadata("Harness Runtime")
            {
                Description = "Live runtime status for the .NET MAUI test harness.",
                Hints = ["harness", "dotnet", "runtime"]
            });

        configurationRootHandle = ReflectionRootRegistry.Register(
            "harness.configuration",
            new HarnessConfigurationReflectionRoot(),
            new ReflectionRootMetadata("Harness Configuration")
            {
                Description = "Static test harness channels and reflection validation metadata.",
                Hints = ["harness", "dotnet", "configuration"]
            },
            ReferenceType.Strong);
    }

    private sealed class HarnessRuntimeReflectionRoot
    {
        public bool RuntimeInitialized => Runtime.IsInitialized;

        public bool RuntimeActive => Runtime.IsActive;

        public bool FramesPerSecondEnabled => Runtime.IsFramesPerSecondEnabled;

        public string HostRuntimeKind => "dotnet";

        public string HarnessName => "Ansight .NET MAUI Test Harness";
    }

    private sealed class HarnessConfigurationReflectionRoot
    {
        public string HostRuntimeKind => "dotnet";

        public string[] RootIds => ["harness.runtime", "harness.configuration"];

        public byte CustomMetricChannelId => CustomAnsightConfiguration.CustomMetricChannelId;

        public byte CustomEventChannelId => CustomAnsightConfiguration.CustomEventChannelId;
    }
}
