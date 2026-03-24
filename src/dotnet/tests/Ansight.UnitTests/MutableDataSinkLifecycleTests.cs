namespace Ansight.UnitTests;

[Collection("RuntimeLifecycle")]
public sealed class MutableDataSinkLifecycleTests
{
    [Fact]
    public void SetAppLifecycleState_UpdatesSnapshotAndEmitsReservedLifecycleEvent()
    {
        var options = Options.CreateBuilder()
            .WithSampleFrequencyMilliseconds(Constants.DefaultSampleFrequencyMilliseconds)
            .WithRetentionPeriodSeconds(Constants.DefaultRetentionPeriodSeconds)
            .Build();
        var dataSink = new MutableDataSink(options);
        var changedAtUtc = DateTimeOffset.UtcNow;

        var didChange = dataSink.SetAppLifecycleState(AppLifecycleState.Foreground, changedAtUtc);
        var snapshot = dataSink.Snapshot();
        var lifecycleChannel = Assert.Single(snapshot.Channels!, channel => channel.Id == Constants.ReservedChannels.Lifecycle_Id);
        var lifecycleEvents = Assert.Single(snapshot.Events!, item => item.ChannelId == Constants.ReservedChannels.Lifecycle_Id).Events!;
        var lifecycleEvent = Assert.Single(lifecycleEvents);

        Assert.True(didChange);
        Assert.Equal(Constants.ReservedChannels.Lifecycle_Name, lifecycleChannel.Name);
        Assert.Equal(AppLifecycleState.Foreground, snapshot.AppState);
        Assert.Equal(changedAtUtc, snapshot.AppStateChangedUtc);
        Assert.Equal(AppEventType.Lifecycle, lifecycleEvent.Type);
        Assert.Equal("App moved to foreground", lifecycleEvent.Label);
        Assert.Equal(Constants.ReservedChannels.Lifecycle_Id, lifecycleEvent.Channel);
        Assert.Equal(changedAtUtc.UtcDateTime, lifecycleEvent.CapturedAtUtc);
    }

    [Fact]
    public void SetAppLifecycleState_IgnoresDuplicateAssignmentsAndDoesNotEmitUnknownTransitionEvent()
    {
        var options = Options.CreateBuilder()
            .WithSampleFrequencyMilliseconds(Constants.DefaultSampleFrequencyMilliseconds)
            .WithRetentionPeriodSeconds(Constants.DefaultRetentionPeriodSeconds)
            .Build();
        var dataSink = new MutableDataSink(options);
        var currentUtc = DateTimeOffset.UtcNow;

        Assert.False(dataSink.SetAppLifecycleState(AppLifecycleState.Unknown, currentUtc.AddSeconds(-2)));
        Assert.True(dataSink.SetAppLifecycleState(AppLifecycleState.Background, currentUtc.AddSeconds(-1)));
        Assert.False(dataSink.SetAppLifecycleState(AppLifecycleState.Background, currentUtc));

        var lifecycleEvents = Assert.Single(dataSink.Snapshot().Events!, item => item.ChannelId == Constants.ReservedChannels.Lifecycle_Id).Events!;
        Assert.Single(lifecycleEvents);
        Assert.Equal("App moved to background", lifecycleEvents[0].Label);
    }
}

[CollectionDefinition("RuntimeLifecycle", DisableParallelization = true)]
public sealed class RuntimeLifecycleCollection
{
}
