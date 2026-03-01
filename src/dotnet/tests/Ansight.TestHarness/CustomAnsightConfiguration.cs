namespace Ansight.TestHarness;

public static class CustomAnsightConfiguration
{
    public const byte CustomMetricChannelId = 96;
    
    public const byte CustomEventChannelId = 128;

    public static readonly List<Channel> AdditionalChannels = new List<Channel>
    {
        new Channel(CustomMetricChannelId, "Custom Metric", new Color(255, 149, 0)),
        new Channel(CustomEventChannelId, "Custom Events", new Color(50, 173, 230)),
    };
}