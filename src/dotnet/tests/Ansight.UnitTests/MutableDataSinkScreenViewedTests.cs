namespace Ansight.UnitTests;

public sealed class MutableDataSinkScreenViewedTests
{
    [Fact]
    public void ScreenViewed_UsesScreenViewedEventTypeOnDetachedChannel()
    {
        var options = Options.CreateBuilder()
            .WithSampleFrequencyMilliseconds(Constants.DefaultSampleFrequencyMilliseconds)
            .WithRetentionPeriodSeconds(Constants.DefaultRetentionPeriodSeconds)
            .Build();
        var dataSink = new MutableDataSink(options);

        dataSink.ScreenViewed("CheckoutPage");

        var screenViewEvent = Assert.Single(dataSink.Events);
        Assert.Equal(AppEventType.ScreenViewed, screenViewEvent.Type);
        Assert.Equal("CheckoutPage", screenViewEvent.Label);
        Assert.Equal(string.Empty, screenViewEvent.Details);
        Assert.Equal(Constants.ReservedChannels.ChannelNotSpecified_Id, screenViewEvent.Channel);
    }

    [Fact]
    public void ScreenViewed_PreservesChannelAndDetails()
    {
        var channel = new Channel(42, "navigation", System.Drawing.Color.CadetBlue);
        var options = Options.CreateBuilder()
            .WithSampleFrequencyMilliseconds(Constants.DefaultSampleFrequencyMilliseconds)
            .WithRetentionPeriodSeconds(Constants.DefaultRetentionPeriodSeconds)
            .AddAdditionalChannel(channel)
            .Build();
        var dataSink = new MutableDataSink(options);

        dataSink.ScreenViewed("SettingsPage", channel.Id, "route=/settings");

        var screenViewEvent = Assert.Single(dataSink.GetEventsForChannel(channel.Id));
        Assert.Equal(AppEventType.ScreenViewed, screenViewEvent.Type);
        Assert.Equal("SettingsPage", screenViewEvent.Label);
        Assert.Equal("route=/settings", screenViewEvent.Details);
        Assert.Equal(channel.Id, screenViewEvent.Channel);
    }
}
