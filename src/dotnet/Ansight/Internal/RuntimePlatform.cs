using System;

namespace Ansight;

/// <summary>
/// Allows platform heads to register platform services without taking direct dependencies in the core runtime implementation.
/// </summary>
public static class RuntimePlatform
{
    private static Func<IFrameRateMonitor>? frameRateMonitorFactory;

    public static void RegisterFrameRateMonitorFactory(Func<IFrameRateMonitor> factory)
    {
        frameRateMonitorFactory = factory;
    }

    public static IFrameRateMonitor CreateFrameRateMonitorFallback(Func<IFrameRateMonitor> defaultFactory)
    {
        return frameRateMonitorFactory?.Invoke() ?? defaultFactory();
    }
}
