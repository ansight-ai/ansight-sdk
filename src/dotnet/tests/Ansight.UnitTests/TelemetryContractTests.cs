using System.Drawing;
using System.Text.Json;
using Ansight.Native;
using Ansight.Telemetry.Events;

namespace Ansight.UnitTests;

public sealed class TelemetryContractTests
{
    [Fact]
    public void FractionalMetrics_PreserveValuesMetadataAndRangeStatistics()
    {
        var channel = new Channel(64, "Frame", Color.Blue, unit: "ms", group: "Render", kind: "duration");
        var options = Options.CreateBuilder().WithAdditionalChannels(new[] { channel }).Build();
        var sink = new MutableDataSink(options);
        sink.Metric(16.67, 64); sink.Metric(-0.125, 64);
        Assert.Equal(new[] { 16.67, -0.125 }, sink.GetMetricsForChannel(64).Select(sample => sample.Value));
        var range = sink.GetMetricsChannelSpanForRange(64, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(-0.125, range.MinValue); Assert.Equal(16.67, range.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => Runtime.Metric(double.NaN, 64));
        var json = NativeRuntimeOptionsJson.Serialize(options);
        using var document = JsonDocument.Parse(json);
        var metadata = document.RootElement.GetProperty("additionalChannels")[0];
        Assert.Equal("ms", metadata.GetProperty("unit").GetString());
        Assert.Equal("Render", metadata.GetProperty("group").GetString());
    }

    [Fact]
    public void Span_HasStableParentAndOneMonotonicEnd()
    {
        var events = new List<string>();
        var span = new TelemetrySpan("Load", 64, "parent", (_, type, channel, details) => {
            Assert.Equal(AppEventType.Span, type); Assert.Equal(64, channel); events.Add(details);
        });
        span.Fail(); span.Dispose(); span.End();
        Assert.Equal(2, events.Count);
        using var start = JsonDocument.Parse(events[0]);
        using var end = JsonDocument.Parse(events[1]);
        Assert.Equal(span.Id, start.RootElement.GetProperty("spanId").GetString());
        Assert.Equal(span.Id, end.RootElement.GetProperty("spanId").GetString());
        Assert.Equal("parent", end.RootElement.GetProperty("parentSpanId").GetString());
        Assert.Equal("error", end.RootElement.GetProperty("status").GetString());
        Assert.True(end.RootElement.GetProperty("durationMs").GetDouble() >= 0);
    }
}
