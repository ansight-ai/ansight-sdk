namespace Ansight.Motion;

/// <summary>Controls app-fed motion evidence without subscribing to device sensors.</summary>
public sealed class MotionCaptureOptions
{
    public bool CaptureShake { get; set; } = true;
    public bool CaptureAccelerometer { get; set; } = true;
    public int MinimumSampleIntervalMilliseconds { get; set; } = 20;

    internal MotionCaptureOptions Validated() => new()
    {
        CaptureShake = CaptureShake,
        CaptureAccelerometer = CaptureAccelerometer,
        MinimumSampleIntervalMilliseconds = Math.Clamp(MinimumSampleIntervalMilliseconds, 10, 1000)
    };
}
