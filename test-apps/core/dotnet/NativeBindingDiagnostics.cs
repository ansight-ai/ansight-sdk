namespace Ansight.TestHarness.Native;

internal static class NativeBindingDiagnostics
{
    internal static string GetStatus()
    {
        try
        {
#if ANDROID
            return Format(
                AI.Ansight.Dotnet.AnsightDotNetBridge.BridgeVersion,
                AI.Ansight.Dotnet.AnsightDotNetBridge.IsInitialized,
                AI.Ansight.Dotnet.AnsightDotNetBridge.IsActive);
#elif IOS || MACCATALYST
            return Format(
                Ansight.Native.Apple.ANSDotNetRuntime.BridgeVersion,
                Ansight.Native.Apple.ANSDotNetRuntime.IsInitialized,
                Ansight.Native.Apple.ANSDotNetRuntime.IsActive);
#else
            return "Native binding unavailable on this target";
#endif
        }
        catch (Exception exception)
        {
            return $"Native binding unavailable ({exception.GetType().Name})";
        }
    }

    private static string Format(string version, bool isInitialized, bool isActive)
    {
        return $"Native binding v{version} • {(isInitialized ? "initialized" : "not initialized")} • {(isActive ? "active" : "inactive")}";
    }
}
