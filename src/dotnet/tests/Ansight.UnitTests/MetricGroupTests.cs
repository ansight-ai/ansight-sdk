using System.Drawing;
using System.Text.Json;
using Ansight.Native;

namespace Ansight.UnitTests;

public sealed class MetricGroupTests
{
    [Fact]
    public void TypedGroup_SurvivesNativeOptions()
    {
        var group = new MetricGroup(" checkout ", " Checkout ");
        var channel = new Channel(64, "Payment latency", group, Color.Blue, unit: "ms");
        var options = Options.CreateBuilder().WithAdditionalChannels([channel]).Build();

        Assert.Equal("checkout", group.Id);
        Assert.Equal("Checkout", channel.Group);
        Assert.Same(group, channel.MetricGroup);

        using var native = JsonDocument.Parse(NativeRuntimeOptionsJson.Serialize(options));
        var nativeChannel = native.RootElement.GetProperty("additionalChannels")[0];
        Assert.Equal("checkout", nativeChannel.GetProperty("metricGroup").GetProperty("id").GetString());
        Assert.Equal("Checkout", nativeChannel.GetProperty("group").GetString());
    }

    [Fact]
    public void LegacyChannels_KeepTheirLabelWithoutInventingAnIdentity()
    {
        var channel = new Channel(64, "Payment latency", Color.Blue, group: "Checkout");

        Assert.Equal("Checkout", channel.Group);
        Assert.Null(channel.MetricGroup);
        Assert.Throws<ArgumentException>(() => new MetricGroup(" ", "Checkout"));
        Assert.Throws<ArgumentException>(() => new MetricGroup("checkout", " "));
    }

    [Fact]
    public void BuiltInChannels_DeclareSharedGroups()
    {
        var sink = new MutableDataSink(Options.CreateBuilder().Build());
        var memory = Assert.Single(sink.Channels, channel => channel.Id == Constants.ReservedChannels.ClrMemoryUsage_Id);
        var fps = Assert.Single(sink.Channels, channel => channel.Id == Constants.ReservedChannels.FramesPerSecond_Id);

        Assert.Equal(MetricGroups.Memory, memory.MetricGroup);
        Assert.Equal(MetricGroups.Rendering, fps.MetricGroup);
    }
}
