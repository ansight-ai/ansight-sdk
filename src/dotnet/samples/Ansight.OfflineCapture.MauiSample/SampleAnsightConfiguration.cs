namespace Ansight.OfflineCapture.MauiSample;

public static class SampleAnsightConfiguration
{
    public const byte SampleMetricChannelId = 96;

    public const byte SampleEventChannelId = 128;

    public const byte SampleInteractionChannelId = 129;

    public static readonly List<Channel> AdditionalChannels =
    [
        new Channel(SampleMetricChannelId, "Offline Sample Metrics", System.Drawing.Color.FromArgb(28, 124, 125)),
        new Channel(SampleEventChannelId, "Offline Sample Events", System.Drawing.Color.FromArgb(189, 76, 59)),
        new Channel(SampleInteractionChannelId, "Offline Interactions", System.Drawing.Color.FromArgb(92, 102, 204))
    ];
}
