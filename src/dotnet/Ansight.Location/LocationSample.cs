namespace Ansight.Location;

/// <summary>An app-observed coordinate supplied explicitly by application code.</summary>
public sealed class LocationSample
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public double? AltitudeMeters { get; init; }

    public double? HorizontalAccuracyMeters { get; init; }

    public double? VerticalAccuracyMeters { get; init; }

    public double? SpeedMetersPerSecond { get; init; }

    public double? HeadingDegrees { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? SampleId { get; init; }

    public string? CorrelationId { get; init; }

    public string? RunId { get; init; }
}
