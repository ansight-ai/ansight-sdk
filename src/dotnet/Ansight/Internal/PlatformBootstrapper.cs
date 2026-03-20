namespace Ansight.Internal;

/// <summary>
/// Ensures platform-specific telemetry services are registered before the runtime is created.
/// </summary>
internal static class PlatformBootstrapper
{
    private static bool configured;

    public static void EnsureConfigured()
    {
        if (configured)
        {
            return;
        }

#if ANDROID
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new FrameRateMonitor());
#elif IOS
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new FrameRateMonitor());
#elif MACCATALYST
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new FrameRateMonitor());
#endif

        configured = true;
    }
}
