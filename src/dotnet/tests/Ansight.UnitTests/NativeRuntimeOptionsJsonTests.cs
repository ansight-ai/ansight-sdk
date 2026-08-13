using System.Drawing;
using System.Text.Json.Nodes;
using Ansight.Native;

namespace Ansight.UnitTests;

public sealed class NativeRuntimeOptionsJsonTests
{
    [Fact]
    public void Serialize_MapsOptionsForNativeBindings()
    {
        var options = Options.CreateBuilder()
            .WithSampleFrequencyMilliseconds(750)
            .WithRetentionPeriodSeconds(120)
            .WithOpenFileHandleTracking()
            .WithJniReferenceCountTracking()
            .AddAdditionalChannel(new Channel(42, "Latency", Color.FromArgb(17, 34, 51)))
            .WithSessionJpegCapture(
                intervalMilliseconds: 1_500,
                quality: 80,
                maxWidth: 640,
                captureGpuBackedSurfaces: false,
                mode: SessionJpegCaptureMode.ScreenshotAndVisualTree)
            .WithTouchCapture(
                captureMoveEvents: false,
                captureCancelEvents: true,
                moveCaptureDistanceThreshold: 6.5,
                moveCaptureFramesPerSecond: 24)
            .WithCrashCapture(new CrashCaptureOptions
            {
                MaximumPendingReports = 4,
                RetentionDays = 3,
                MaximumBreadcrumbs = 20,
                MaximumTraceBytes = 64 * 1024
            })
            .RegisterCustomProperty("flags", "beta", true)
            .RegisterCustomProperty("limits", "count", 5)
            .Build();

        var json = JsonNode.Parse(NativeRuntimeOptionsJson.Serialize(options))!.AsObject();

        Assert.Equal(750, json["sampleFrequencyMilliseconds"]!.GetValue<int>());
        Assert.Equal(120, json["retentionPeriodSeconds"]!.GetValue<int>());
        Assert.True(json["enableOpenFileHandleTracking"]!.GetValue<bool>());
        Assert.True(json["enableJniReferenceCountTracking"]!.GetValue<bool>());
        Assert.Equal(42, json["additionalChannels"]![0]!["id"]!.GetValue<int>());
        Assert.Equal("#112233", json["additionalChannels"]![0]!["color"]!.GetValue<string>());
        Assert.Equal(1_500, json["sessionJpegCapture"]!["intervalMilliseconds"]!.GetValue<int>());
        Assert.False(json["sessionJpegCapture"]!["captureGpuBackedSurfaces"]!.GetValue<bool>());
        Assert.Equal("screenshotAndVisualTree", json["sessionJpegCapture"]!["mode"]!.GetValue<string>());
        Assert.False(json["touchCapture"]!["captureMoveEvents"]!.GetValue<bool>());
        Assert.Equal(6.5, json["touchCapture"]!["moveCaptureDistanceThreshold"]!.GetValue<double>());
        Assert.True(json["crashCapture"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(4, json["crashCapture"]!["maximumPendingReports"]!.GetValue<int>());
        Assert.Equal(64 * 1024, json["crashCapture"]!["maximumTraceBytes"]!.GetValue<int>());
        Assert.Equal("true", json["customProperties"]!["flags"]!["beta"]!.GetValue<string>());
        Assert.Equal("5", json["customProperties"]!["limits"]!["count"]!.GetValue<string>());
        Assert.True(json["hostAutoProbe"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(
            "ai.ansight.dotnet.saved-pairing",
            json["hostConnection"]!["savedConfigKey"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_PreservesDisabledCaptureOptions()
    {
        var options = Options.CreateBuilder()
            .WithoutSessionJpegCapture()
            .WithoutTouchCapture()
            .WithoutCrashCapture()
            .Build();

        var json = JsonNode.Parse(NativeRuntimeOptionsJson.Serialize(options))!.AsObject();

        Assert.Null(json["sessionJpegCapture"]);
        Assert.Null(json["touchCapture"]);
        Assert.False(json["enableOpenFileHandleTracking"]!.GetValue<bool>());
        Assert.False(json["enableJniReferenceCountTracking"]!.GetValue<bool>());
        Assert.False(json["crashCapture"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Serialize_MapsTouchVisualTreeCaptureMode()
    {
        var options = Options.CreateBuilder()
            .WithSessionJpegCapture(mode: SessionJpegCaptureMode.ScreenshotWithVisualTreeOnTouch)
            .Build();

        var json = JsonNode.Parse(NativeRuntimeOptionsJson.Serialize(options))!.AsObject();

        Assert.Equal(
            "screenshotWithVisualTreeOnTouch",
            json["sessionJpegCapture"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public void SessionJpegCapture_PreservesGpuBackedSurfaceOptionAcrossBuilders()
    {
        var configuredOptions = Options.CreateBuilder()
            .WithSessionJpegCapture(new SessionJpegCaptureOptions
            {
                IntervalMilliseconds = 1_000,
                Quality = 50,
                MaxWidth = 480,
                CaptureGpuBackedSurfaces = false,
                Mode = SessionJpegCaptureMode.ScreenshotAndVisualTree
            })
            .Build();

        var copiedOptions = Options.CreateBuilder(configuredOptions).Build();

        Assert.NotNull(copiedOptions.SessionJpegCapture);
        Assert.False(copiedOptions.SessionJpegCapture.CaptureGpuBackedSurfaces);
        Assert.Equal(SessionJpegCaptureMode.ScreenshotAndVisualTree, copiedOptions.SessionJpegCapture.Mode);
    }

    [Fact]
    public void ParseTelemetrySnapshot_MapsNativeRecordsAndSequences()
    {
        const string json = """
            {
              "metrics": [
                {
                  "value": 512,
                  "channel": 2,
                  "capturedAtUtc": "2026-07-30T12:00:00.000Z",
                  "capturedAtEpochMs": 1785412800000,
                  "sequence": 7
                }
              ],
              "events": [
                {
                  "label": "checkout",
                  "type": "ScreenViewed",
                  "details": "route=/checkout",
                  "channel": 4,
                  "capturedAtUtc": "2026-07-30T12:00:01.000Z",
                  "capturedAtEpochMs": 1785412801000,
                  "externalId": "navigation-42",
                  "sequence": 9
                }
              ]
            }
            """;

        var snapshot = NativeRuntimeJson.ParseTelemetrySnapshot(json);

        var metric = Assert.Single(snapshot.Metrics);
        Assert.Equal(512, metric.Value);
        Assert.Equal((byte)2, metric.Channel);
        Assert.Equal(7, metric.Sequence);

        var nativeEvent = Assert.Single(snapshot.Events);
        Assert.Equal("checkout", nativeEvent.Label);
        Assert.Equal(AppEventType.ScreenViewed, nativeEvent.Type);
        Assert.Equal("route=/checkout", nativeEvent.Details);
        Assert.Equal("navigation-42", nativeEvent.ExternalId);
        Assert.Equal(9, nativeEvent.Sequence);
    }
}
