namespace Ansight.Connectivity;

public sealed class ConnectivityCaptureOptions
{
    /// <summary>Minimum interval between quality-only updates. State changes are always emitted.</summary>
    public int MinimumSampleIntervalMilliseconds { get; set; } = 5_000;

    internal ConnectivityCaptureOptions Validated()
        => new() { MinimumSampleIntervalMilliseconds = Math.Clamp(MinimumSampleIntervalMilliseconds, 1_000, 300_000) };
}
