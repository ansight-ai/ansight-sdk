namespace Ansight.Location;

/// <summary>Builds observed-location precision, sampling, and deduplication controls.</summary>
public sealed class LocationCaptureOptionsBuilder
{
    private readonly LocationCaptureOptions options = new();

    public LocationCaptureOptionsBuilder WithPrecision(int decimalPlaces)
    {
        options.DecimalPlaces = decimalPlaces;
        return this;
    }

    public LocationCaptureOptionsBuilder WithMinimumInterval(TimeSpan interval)
    {
        options.MinimumInterval = interval;
        return this;
    }

    public LocationCaptureOptionsBuilder WithMinimumDistance(double meters)
    {
        options.MinimumDistanceMeters = meters;
        return this;
    }

    internal LocationCaptureOptions Build() => options.Normalize();
}
