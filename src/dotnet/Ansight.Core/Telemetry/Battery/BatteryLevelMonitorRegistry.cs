using System;

namespace Ansight.Telemetry.Battery;

internal static class BatteryLevelMonitorRegistry
{
    private static Func<IBatteryLevelMonitor>? batteryLevelMonitorFactory;
    private static bool? isSupported;

    public static bool? IsSupported => isSupported;

    public static void RegisterFactory(Func<IBatteryLevelMonitor> factory, bool supported)
    {
        batteryLevelMonitorFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        isSupported = supported;
    }

    public static IBatteryLevelMonitor Create(Func<IBatteryLevelMonitor> defaultFactory)
    {
        return batteryLevelMonitorFactory?.Invoke() ?? defaultFactory();
    }

    internal static void Reset()
    {
        batteryLevelMonitorFactory = null;
        isSupported = null;
    }
}
