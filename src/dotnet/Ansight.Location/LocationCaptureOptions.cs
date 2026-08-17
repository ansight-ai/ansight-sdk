namespace Ansight.Location;

/// <summary>Privacy and volume controls applied before an observed location leaves the app.</summary>
public sealed class LocationCaptureOptions
{
    public int DecimalPlaces { get; internal set; } = 5;

    public TimeSpan MinimumInterval { get; internal set; } = TimeSpan.FromSeconds(1);

    public double MinimumDistanceMeters { get; internal set; } = 1;

    internal LocationCaptureOptions Normalize()
    {
        DecimalPlaces = Math.Clamp(DecimalPlaces, 0, 7);
        MinimumInterval = MinimumInterval < TimeSpan.Zero ? TimeSpan.Zero : MinimumInterval;
        MinimumDistanceMeters = !double.IsFinite(MinimumDistanceMeters) || MinimumDistanceMeters < 0
            ? 0
            : MinimumDistanceMeters;
        return this;
    }
}
