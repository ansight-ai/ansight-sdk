using System.Diagnostics.Metrics;
using Ansight.Telemetry.Channels;
using Ansight.Tools.Avalonia;
using Xunit;

namespace Ansight.Avalonia.Tests;

public sealed class AvaloniaRenderingMetricsTests
{
    [Fact]
    public void PublishesMeasuredDurationsCountsGaugesAndRenderRate()
    {
        var samples = new List<MetricSample>();
        using var collector = new AvaloniaRenderingMetrics((value, channel) => samples.Add(new MetricSample(channel, value)), () => true);
        collector.Start();
        using var meter = new Meter("Avalonia.Diagnostic.Meter");
        var render = meter.CreateHistogram<double>("avalonia.comp.render.time", "ms");
        meter.CreateObservableUpDownCounter("avalonia.ui.visual.count", () => 17);
        render.Record(2);
        render.Record(6);
        render.Record(double.NaN);
        render.Record(-1);
        collector.Flush();

        Assert.Equal(4, Assert.Single(samples, sample => sample.Channel == 40).Value);
        Assert.Equal(6, Assert.Single(samples, sample => sample.Channel == 41).Value);
        Assert.Equal(2, Assert.Single(samples, sample => sample.Channel == 42).Value);
        Assert.Equal(17, Assert.Single(samples, sample => sample.Channel == 59).Value);
        Assert.True(Assert.Single(samples, sample => sample.Channel == 61).Value > 0);
        Assert.Equal("ms", collector.Channels.Single(channel => channel.Id == 40).Unit);
        Assert.Equal("fps", collector.Channels.Single(channel => channel.Id == 61).Unit);
        Assert.Equal(MetricGroups.Rendering, collector.Channels.Single(channel => channel.Id == 40).MetricGroup);
        Assert.Equal(MetricGroups.Layout, collector.Channels.Single(channel => channel.Id == 46).MetricGroup);
        Assert.Equal(MetricGroups.Runtime, collector.Channels.Single(channel => channel.Id == 59).MetricGroup);
        Assert.All(collector.Channels, channel => Assert.NotNull(channel.MetricGroup));
        Assert.Equal(22, collector.Channels.Select(channel => channel.Id).Distinct().Count());

        samples.Clear();
        collector.Flush();

        Assert.Equal(0, Assert.Single(samples, sample => sample.Channel == 61).Value);
        Assert.DoesNotContain(samples, sample => sample.Channel is 40 or 41 or 42);
    }

    [Fact]
    public void DoesNotPublishUnavailableInactiveOrDisposedMeasurements()
    {
        var samples = new List<MetricSample>();
        var active = false;
        using var collector = new AvaloniaRenderingMetrics((value, channel) => samples.Add(new MetricSample(channel, value)), () => active);
        collector.Start();
        using var unrelated = new Meter("Another.Meter");
        unrelated.CreateHistogram<double>("avalonia.comp.render.time", "ms").Record(99);
        collector.Flush();

        Assert.Empty(samples);

        using var meter = new Meter("Avalonia.Diagnostic.Meter");
        var layout = meter.CreateHistogram<double>("avalonia.ui.measure.time", "ms");
        layout.Record(7);
        collector.Flush();
        Assert.Empty(samples);

        active = true;
        layout.Record(3);
        collector.Flush();
        Assert.Equal(3, Assert.Single(samples, sample => sample.Channel == 46).Value);
        Assert.DoesNotContain(samples, sample => sample.Channel == 61);

        collector.Dispose();
        samples.Clear();
        layout.Record(8);
        collector.Flush();
        Assert.Empty(samples);
    }

    private sealed record MetricSample(byte Channel, double Value);
}
