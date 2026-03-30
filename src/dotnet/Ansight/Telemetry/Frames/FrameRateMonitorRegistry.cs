using System;

namespace Ansight.Telemetry.Frames;

internal static class FrameRateMonitorRegistry
{
    private static Func<IFrameRateMonitor>? frameRateMonitorFactory;

    public static void RegisterFactory(Func<IFrameRateMonitor> factory)
    {
        frameRateMonitorFactory = factory;
    }

    public static IFrameRateMonitor Create(Func<IFrameRateMonitor> defaultFactory)
    {
        return frameRateMonitorFactory?.Invoke() ?? defaultFactory();
    }
}
