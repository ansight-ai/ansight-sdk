using System.Drawing;

namespace Ansight.UnitTests;

public sealed class DefaultRuntimeChannelTests
{
    [Theory]
    [InlineData(Constants.ReservedChannels.JniReferenceCount_Id, Constants.ReservedChannels.JniReferenceCount_Name)]
    [InlineData(Constants.ReservedChannels.OpenFileHandles_Id, Constants.ReservedChannels.OpenFileHandles_Name)]
    public void RuntimeDiagnosticChannels_AreReserved(byte id, string name)
    {
        var channel = new Channel(id, name, Color.Empty);

        Assert.True(Constants.IsPredefinedChannel(channel));
    }

    [Fact]
    public void RuntimeDiagnosticTracking_DefaultsOffAndCanBeDisabledAgain()
    {
        var defaults = Options.CreateBuilder().Build();

        Assert.False(defaults.EnableOpenFileHandleTracking);
        Assert.False(defaults.EnableJniReferenceCountTracking);

        var disabled = Options.CreateBuilder()
            .WithOpenFileHandleTracking()
            .WithJniReferenceCountTracking()
            .WithoutOpenFileHandleTracking()
            .WithoutJniReferenceCountTracking()
            .Build();

        Assert.False(disabled.EnableOpenFileHandleTracking);
        Assert.False(disabled.EnableJniReferenceCountTracking);
    }

#if ANDROID
    [Fact]
    public void RuntimeDiagnosticChannels_ArePresentWhenEnabledOnAndroid()
    {
        var dataSink = new MutableDataSink(
            Options.CreateBuilder()
                .WithOpenFileHandleTracking()
                .WithJniReferenceCountTracking()
                .Build());

        Assert.Contains(
            dataSink.Channels,
            channel => channel.Id == Constants.ReservedChannels.JniReferenceCount_Id &&
                       channel.Name == Constants.ReservedChannels.JniReferenceCount_Name);
        Assert.Contains(
            dataSink.Channels,
            channel => channel.Id == Constants.ReservedChannels.OpenFileHandles_Id &&
                       channel.Name == Constants.ReservedChannels.OpenFileHandles_Name);
    }
#endif
}
