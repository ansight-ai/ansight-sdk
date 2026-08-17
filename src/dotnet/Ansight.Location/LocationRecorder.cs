using System.Text.Json.Nodes;
using Ansight.Pairing;

namespace Ansight.Location;

internal sealed class LocationRecorder
{
    internal const string EventType = "CLIENT_LOCATION";
    internal const string Schema = "ansight.location.sample.v1";
    private readonly IRuntime runtime;
    private readonly LocationCaptureOptions options;
    private readonly Lock gate = new();
    private LocationSample? lastEmittedSample;

    internal LocationRecorder(IRuntime runtime, LocationCaptureOptions options)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.options = options?.Normalize() ?? throw new ArgumentNullException(nameof(options));
    }

    internal async Task<OperationResult> RecordAsync(
        LocationSample sample,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        Validate(sample);

        var normalized = Normalize(sample);
        lock (gate)
        {
            if (ShouldSuppress(normalized))
            {
                return OperationResult.FromSuccess("Observed location suppressed by sampling controls.");
            }

            lastEmittedSample = normalized;
        }

        var payload = new JsonObject
        {
            ["schema"] = Schema,
            ["sampleId"] = normalized.SampleId,
            ["capturedAtUtc"] = normalized.CapturedAtUtc,
            ["source"] = "app_observed",
            ["latitude"] = normalized.Latitude,
            ["longitude"] = normalized.Longitude,
            ["altitudeMeters"] = normalized.AltitudeMeters,
            ["horizontalAccuracyMeters"] = normalized.HorizontalAccuracyMeters,
            ["verticalAccuracyMeters"] = normalized.VerticalAccuracyMeters,
            ["speedMetersPerSecond"] = normalized.SpeedMetersPerSecond,
            ["headingDegrees"] = normalized.HeadingDegrees,
            ["correlationId"] = normalized.CorrelationId,
            ["runId"] = normalized.RunId
        };
        return await runtime.SendSessionEventAsync(EventType, payload, cancellationToken);
    }

    private bool ShouldSuppress(LocationSample sample)
    {
        if (lastEmittedSample is null)
        {
            return false;
        }

        return sample.CapturedAtUtc - lastEmittedSample.CapturedAtUtc < options.MinimumInterval
               || LocationMath.DistanceMeters(lastEmittedSample, sample) < options.MinimumDistanceMeters;
    }

    private LocationSample Normalize(LocationSample sample) => new()
    {
        Latitude = Math.Round(sample.Latitude, options.DecimalPlaces, MidpointRounding.AwayFromZero),
        Longitude = Math.Round(sample.Longitude, options.DecimalPlaces, MidpointRounding.AwayFromZero),
        AltitudeMeters = NormalizeFinite(sample.AltitudeMeters),
        HorizontalAccuracyMeters = NormalizeNonNegative(sample.HorizontalAccuracyMeters),
        VerticalAccuracyMeters = NormalizeNonNegative(sample.VerticalAccuracyMeters),
        SpeedMetersPerSecond = NormalizeNonNegative(sample.SpeedMetersPerSecond),
        HeadingDegrees = NormalizeFinite(sample.HeadingDegrees),
        CapturedAtUtc = sample.CapturedAtUtc.ToUniversalTime(),
        SampleId = string.IsNullOrWhiteSpace(sample.SampleId) ? Guid.NewGuid().ToString("N") : sample.SampleId.Trim(),
        CorrelationId = NormalizeText(sample.CorrelationId),
        RunId = NormalizeText(sample.RunId)
    };

    private static void Validate(LocationSample sample)
    {
        if (!double.IsFinite(sample.Latitude) || sample.Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(sample), "Latitude must be between -90 and 90.");
        }
        if (!double.IsFinite(sample.Longitude) || sample.Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(sample), "Longitude must be between -180 and 180.");
        }
    }

    private static double? NormalizeFinite(double? value)
        => value.HasValue && double.IsFinite(value.Value) ? value : null;

    private static double? NormalizeNonNegative(double? value)
        => value.HasValue && double.IsFinite(value.Value) && value.Value >= 0 ? value : null;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
