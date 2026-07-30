namespace Ansight.Native;

internal static class NativeRuntimeBridgeFactory
{
    internal static INativeRuntimeBridge Create(Options options)
    {
#if ANDROID
        return Initialize(new AndroidNativeRuntimeBridge(), options);
#elif IOS || MACCATALYST
        return Initialize(new AppleNativeRuntimeBridge(), options);
#else
        return NullNativeRuntimeBridge.Instance;
#endif
    }

    private static INativeRuntimeBridge Initialize(INativeRuntimeBridge bridge, Options options)
    {
        try
        {
            bridge.Initialize(options);
            Logger.Info($"Initialized native Ansight bridge version {bridge.BridgeVersion}.");
            return bridge;
        }
        catch (Exception exception)
        {
            Logger.Exception(exception);
            throw new InvalidOperationException(
                "The native Ansight runtime is required on Android, iOS, and Mac Catalyst but could not be initialized.",
                exception);
        }
    }
}
