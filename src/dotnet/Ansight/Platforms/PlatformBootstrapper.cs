using Ansight.Telemetry.Frames;

namespace Ansight.Platforms;

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
        FrameRateMonitorRegistry.RegisterFactory(() => new FrameRateMonitor());
#elif IOS
        FrameRateMonitorRegistry.RegisterFactory(() => new FrameRateMonitor());
#elif MACCATALYST
        FrameRateMonitorRegistry.RegisterFactory(() => new FrameRateMonitor());
#endif

        configured = true;
    }
}
