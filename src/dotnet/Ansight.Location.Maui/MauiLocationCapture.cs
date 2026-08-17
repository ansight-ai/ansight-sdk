using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;
using Ansight.Pairing;

namespace Ansight.Location.Maui;

/// <summary>Converts an app-owned MAUI Essentials location into an Ansight observation.</summary>
public static class MauiLocationCapture
{
    public static Task<OperationResult> RecordAsync(
        MauiLocation location,
        string? correlationId = null,
        string? runId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        return global::Ansight.Location.LocationCapture.RecordAsync(
            new global::Ansight.Location.LocationSample
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                AltitudeMeters = location.Altitude,
                HorizontalAccuracyMeters = location.Accuracy,
                VerticalAccuracyMeters = location.VerticalAccuracy,
                SpeedMetersPerSecond = location.Speed,
                HeadingDegrees = location.Course,
                CapturedAtUtc = location.Timestamp,
                CorrelationId = correlationId,
                RunId = runId
            },
            cancellationToken);
    }
}
