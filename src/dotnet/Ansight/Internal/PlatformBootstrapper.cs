namespace Ansight;

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
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new AndroidFrameRateMonitor());
#elif IOS
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new IosFrameRateMonitor());
#elif MACCATALYST
        RuntimePlatform.RegisterFrameRateMonitorFactory(() => new MacCatalystFrameRateMonitor());
#endif

        configured = true;
    }
}
