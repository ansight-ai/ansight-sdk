namespace Ansight.Telemetry.Frames;

/// <summary>
/// Platform-specific frame rate monitor.
/// </summary>
public interface IFrameRateMonitor : IDisposable
{
    void Start();
    void Stop();

    /// <summary>
    /// Returns the most recent frames-per-second measurement and clears the consumed value.
    /// </summary>
    int ConsumeFramesPerSecond();
}

internal static class FrameRateMonitorFactory
{
    public static IFrameRateMonitor Create()
    {
        return RuntimePlatform.CreateFrameRateMonitorFallback(() =>
        {
#if ANDROID || IOS || MACCATALYST
            return new FrameRateMonitor();
#else
            return new NoopFrameRateMonitor();
#endif
        });
    }

    private sealed class NoopFrameRateMonitor : IFrameRateMonitor
    {
        public void Start()
        {
        }

        public void Stop()
        {
        }

        public int ConsumeFramesPerSecond() => 0;

        public void Dispose()
        {
        }
    }
}
