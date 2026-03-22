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
        var changedAtUtc = DateTimeOffset.Parse("2026-03-22T01:02:03Z");

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

        Assert.False(dataSink.SetAppLifecycleState(AppLifecycleState.Unknown, DateTimeOffset.Parse("2026-03-22T01:00:00Z")));
        Assert.True(dataSink.SetAppLifecycleState(AppLifecycleState.Background, DateTimeOffset.Parse("2026-03-22T01:01:00Z")));
        Assert.False(dataSink.SetAppLifecycleState(AppLifecycleState.Background, DateTimeOffset.Parse("2026-03-22T01:02:00Z")));

        var lifecycleEvents = Assert.Single(dataSink.Snapshot().Events!, item => item.ChannelId == Constants.ReservedChannels.Lifecycle_Id).Events!;
        Assert.Single(lifecycleEvents);
        Assert.Equal("App moved to background", lifecycleEvents[0].Label);
    }
}

[CollectionDefinition("RuntimeLifecycle", DisableParallelization = true)]
public sealed class RuntimeLifecycleCollection
{
}
