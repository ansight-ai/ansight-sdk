namespace Ansight.TestHarness;

public static class CustomAnsightConfiguration
{
    public const string ClientName = "Ansight .NET MAUI Harness";

    public const byte CustomMetricChannelId = 96;
    
    public const byte CustomEventChannelId = 128;

    public static readonly List<Channel> AdditionalChannels = new List<Channel>
    {
        new Channel(CustomMetricChannelId, "Custom Metric", System.Drawing.Color.FromArgb(255, 149, 0)),
        new Channel(CustomEventChannelId, "Custom Events", System.Drawing.Color.FromArgb(50, 173, 230)),
    };
}
