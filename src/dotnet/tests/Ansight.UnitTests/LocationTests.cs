using Ansight.Location;

namespace Ansight.UnitTests;

public sealed class LocationTests
{
    [Fact]
    public void LocationFeature_IsAbsentUnlessExplicitlyRegistered()
    {
        var excluded = Options.CreateBuilder().WithAnsightSdk().Build();
        var enabled = Options.CreateBuilder().WithAnsightSdk().WithObservedLocationCapture().Build();

        Assert.DoesNotContain(excluded.RuntimeFeatures, feature => feature.Id == "location");
        Assert.Contains(enabled.RuntimeFeatures, feature => feature.Id == "location");
    }

    [Fact]
    public async Task Recorder_EmitsOnExistingRuntimeAndAppliesPrecisionAndDeduplication()
    {
        var runtime = new LocationTestRuntime();
        var options = new LocationCaptureOptionsBuilder()
            .WithPrecision(3)
            .WithMinimumInterval(TimeSpan.FromSeconds(5))
            .WithMinimumDistance(10)
            .Build();
        var recorder = new LocationRecorder(runtime, options);
        var capturedAtUtc = DateTimeOffset.Parse("2026-08-17T01:00:00Z");

        var first = await recorder.RecordAsync(new LocationSample
        {
            SampleId = "sample-1",
            CapturedAtUtc = capturedAtUtc,
            Latitude = -33.868812,
            Longitude = 151.209319,
            CorrelationId = "command-1",
            RunId = "run-1"
        }, CancellationToken.None);
        var duplicate = await recorder.RecordAsync(new LocationSample
        {
            SampleId = "sample-2",
            CapturedAtUtc = capturedAtUtc.AddSeconds(1),
            Latitude = -33.868812,
            Longitude = 151.209319
        }, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(duplicate.Success);
        Assert.Single(runtime.Events);
        var emitted = runtime.Events[0];
        Assert.Equal("CLIENT_LOCATION", emitted.Type);
        Assert.Equal("app_observed", emitted.Payload["source"]!.GetValue<string>());
        Assert.Equal(-33.869, emitted.Payload["latitude"]!.GetValue<double>());
        Assert.Equal(151.209, emitted.Payload["longitude"]!.GetValue<double>());
        Assert.Equal("command-1", emitted.Payload["correlationId"]!.GetValue<string>());
    }

}
