namespace Ansight.Telemetry.Battery;

/// <summary>
/// Platform-specific battery level monitor.
/// </summary>
internal interface IBatteryLevelMonitor : IDisposable
{
    bool IsSupported { get; }

    void Start();

    void Stop();

    /// <summary>
    /// Reads the current battery charge level as a percentage from 0 to 100.
    /// </summary>
    long? ReadBatteryLevelPercentage();
}

internal static class BatteryLevelMonitorFactory
{
    public static bool IsSupported => BatteryLevelMonitorRegistry.IsSupported ?? PlatformDefaultIsSupported;

    public static IBatteryLevelMonitor Create()
    {
        return BatteryLevelMonitorRegistry.Create(() =>
        {
#if ANDROID || IOS || MACCATALYST
            return new BatteryLevelMonitor();
#else
            return new NoopBatteryLevelMonitor();
#endif
        });
    }

#if ANDROID || IOS || MACCATALYST
    private const bool PlatformDefaultIsSupported = true;
#else
    private const bool PlatformDefaultIsSupported = false;
#endif

    private sealed class NoopBatteryLevelMonitor : IBatteryLevelMonitor
    {
        public bool IsSupported => false;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public long? ReadBatteryLevelPercentage() => null;

        public void Dispose()
        {
        }
    }
}
