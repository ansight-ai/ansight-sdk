namespace Ansight.Telemetry.Data;

internal sealed class RuntimeDataSink : IDataSink
{
    private readonly IDataSink storage;
    private readonly IRuntime runtime;

    internal RuntimeDataSink(IDataSink storage, IRuntime runtime)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public IReadOnlyList<Channel> Channels => storage.Channels;

    public IReadOnlyList<Metric> Metrics => storage.Metrics;

    public IReadOnlyList<AppEvent> Events => storage.Events;

    public event EventHandler<MetricsUpdatedEventArgs>? OnMetricsUpdated
    {
        add => storage.OnMetricsUpdated += value;
        remove => storage.OnMetricsUpdated -= value;
    }

    public event EventHandler<AppEventsUpdatedEventArgs>? OnEventsUpdated
    {
        add => storage.OnEventsUpdated += value;
        remove => storage.OnEventsUpdated -= value;
    }

    public IReadOnlyList<Metric> GetMetricsForChannel(Channel channel)
        => storage.GetMetricsForChannel(channel);

    public IReadOnlyList<Metric> GetMetricsForChannel(byte channelId)
        => storage.GetMetricsForChannel(channelId);

    public IReadOnlyList<Metric> GetMetricsForChannelInRange(
        Channel channel,
        DateTime fromUtc,
        DateTime toUtc)
        => storage.GetMetricsForChannelInRange(channel, fromUtc, toUtc);

    public IReadOnlyList<Metric> GetMetricsForChannelInRange(
        byte channelId,
        DateTime fromUtc,
        DateTime toUtc)
        => storage.GetMetricsForChannelInRange(channelId, fromUtc, toUtc);

    public ChannelSpan GetMetricsChannelSpanForRange(
        byte channelId,
        DateTime fromUtc,
        DateTime toUtc)
        => storage.GetMetricsChannelSpanForRange(channelId, fromUtc, toUtc);

    public void UseMetricsInChannelForRange(
        byte channelId,
        DateTime fromUtc,
        DateTime toUtc,
        Action<ReadOnlySpan<Metric>> useAction)
        => storage.UseMetricsInChannelForRange(channelId, fromUtc, toUtc, useAction);

    public IReadOnlyList<AppEvent> GetEventsForChannel(Channel channel)
        => storage.GetEventsForChannel(channel);

    public IReadOnlyList<AppEvent> GetEventsForChannel(byte channelId)
        => storage.GetEventsForChannel(channelId);

    public IReadOnlyList<AppEvent> GetEventsForChannelInRange(
        Channel channel,
        DateTime fromUtc,
        DateTime toUtc)
        => storage.GetEventsForChannelInRange(channel, fromUtc, toUtc);

    public IReadOnlyList<AppEvent> GetEventsForChannelInRange(
        byte channelId,
        DateTime fromUtc,
        DateTime toUtc)
        => storage.GetEventsForChannelInRange(channelId, fromUtc, toUtc);

    public void UseEventsInChannelForRange(
        byte channelId,
        DateTime fromUtc,
        DateTime toUtc,
        Action<ReadOnlySpan<AppEvent>> useAction)
        => storage.UseEventsInChannelForRange(channelId, fromUtc, toUtc, useAction);

    public void Event(string label) => runtime.Event(label);

    public void Event(string label, AppEventType type) => runtime.Event(label, type);

    public void Event(string label, AppEventType type, string details)
        => runtime.Event(label, type, details);

    public void Event(string label, byte channel) => runtime.Event(label, channel);

    public void Event(string label, AppEventType type, byte channel)
        => runtime.Event(label, type, channel);

    public void Event(string label, AppEventType type, byte channel, string details)
        => runtime.Event(label, type, channel, details);

    public void ScreenViewed(string screenName) => runtime.ScreenViewed(screenName);

    public void ScreenViewed(string screenName, string details)
        => runtime.ScreenViewed(screenName, details);

    public void ScreenViewed(string screenName, byte channel)
        => runtime.ScreenViewed(screenName, channel);

    public void ScreenViewed(string screenName, byte channel, string details)
        => runtime.ScreenViewed(screenName, channel, details);

    public Snapshot Snapshot() => storage.Snapshot();
}
