using System.Drawing;
using Ansight;

namespace Ansight.IntegrationTests.Support;

internal sealed class TestDataSink : IDataSink, IAppLifecycleStateSource
{
    private readonly List<Channel> channels;
    private readonly List<Metric> metrics;
    private readonly List<AppEvent> events;
    private AppLifecycleState currentAppLifecycleState = AppLifecycleState.Unknown;
    private DateTimeOffset? currentAppLifecycleStateChangedUtc;

    public TestDataSink(
        IEnumerable<Channel> channels,
        IEnumerable<Metric>? metrics = null,
        IEnumerable<AppEvent>? events = null)
    {
        this.channels = channels.ToList();
        this.metrics = metrics?.OrderBy(metric => metric.CapturedAtUtc).ToList() ?? [];
        this.events = events?.OrderBy(@event => @event.CapturedAtUtc).ToList() ?? [];
    }

    public IReadOnlyList<Channel> Channels => channels;

    public IReadOnlyList<Metric> Metrics => metrics.ToArray();

    public IReadOnlyList<AppEvent> Events => events.ToArray();

    public AppLifecycleState CurrentAppLifecycleState => currentAppLifecycleState;

    public DateTimeOffset? CurrentAppLifecycleStateChangedUtc => currentAppLifecycleStateChangedUtc;

    public event EventHandler<MetricsUpdatedEventArgs>? OnMetricsUpdated;

    public event EventHandler<AppEventsUpdatedEventArgs>? OnEventsUpdated;

    public event EventHandler<AppLifecycleStateChangedEventArgs>? AppLifecycleStateChanged;

    public void AddMetric(Metric metric)
    {
        metrics.Add(metric);
        metrics.Sort();
        OnMetricsUpdated?.Invoke(this, new MetricsUpdatedEventArgs([metric], Array.Empty<Metric>()));
    }

    public void AddEvent(AppEvent @event)
    {
        events.Add(@event);
        events.Sort();
        OnEventsUpdated?.Invoke(this, new AppEventsUpdatedEventArgs([@event], Array.Empty<AppEvent>()));
    }

    public void SetAppLifecycleState(AppLifecycleState state, DateTimeOffset? changedAtUtc = null)
    {
        if (currentAppLifecycleState == state)
        {
            return;
        }

        currentAppLifecycleState = state;
        currentAppLifecycleStateChangedUtc = (changedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        AppLifecycleStateChanged?.Invoke(this, new AppLifecycleStateChangedEventArgs(state, currentAppLifecycleStateChangedUtc));
    }

    public IReadOnlyList<Metric> GetMetricsForChannel(Channel channel) => GetMetricsForChannel(channel.Id);

    public IReadOnlyList<Metric> GetMetricsForChannel(byte channelId)
        => metrics.Where(metric => metric.Channel == channelId).ToArray();

    public IReadOnlyList<Metric> GetMetricsForChannelInRange(Channel channel, DateTime fromUtc, DateTime toUtc)
        => GetMetricsForChannelInRange(channel.Id, fromUtc, toUtc);

    public IReadOnlyList<Metric> GetMetricsForChannelInRange(byte channelId, DateTime fromUtc, DateTime toUtc)
        => metrics.Where(metric => metric.Channel == channelId && metric.CapturedAtUtc >= fromUtc && metric.CapturedAtUtc <= toUtc).ToArray();

    public ChannelSpan GetMetricsChannelSpanForRange(byte channelId, DateTime fromUtc, DateTime toUtc)
    {
        var metrics = GetMetricsForChannelInRange(channelId, fromUtc, toUtc);
        return new ChannelSpan
        {
            ChannelId = channelId,
            MinValue = metrics.Count == 0 ? 0 : metrics.Min(metric => metric.Value),
            MaxValue = metrics.Count == 0 ? 0 : metrics.Max(metric => metric.Value),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Count = metrics.Count,
            Valid = metrics.Count > 0
        };
    }

    public void UseMetricsInChannelForRange(byte channelId, DateTime fromUtc, DateTime toUtc, Action<ReadOnlySpan<Metric>> useAction)
    {
        var metrics = GetMetricsForChannelInRange(channelId, fromUtc, toUtc).ToArray();
        useAction(metrics.AsSpan());
    }

    public IReadOnlyList<AppEvent> GetEventsForChannel(Channel channel) => GetEventsForChannel(channel.Id);

    public IReadOnlyList<AppEvent> GetEventsForChannel(byte channelId)
        => events.Where(@event => @event.Channel == channelId).ToArray();

    public IReadOnlyList<AppEvent> GetEventsForChannelInRange(Channel channel, DateTime fromUtc, DateTime toUtc)
        => GetEventsForChannelInRange(channel.Id, fromUtc, toUtc);

    public IReadOnlyList<AppEvent> GetEventsForChannelInRange(byte channelId, DateTime fromUtc, DateTime toUtc)
        => events.Where(@event => @event.Channel == channelId && @event.CapturedAtUtc >= fromUtc && @event.CapturedAtUtc <= toUtc).ToArray();

    public void UseEventsInChannelForRange(byte channelId, DateTime fromUtc, DateTime toUtc, Action<ReadOnlySpan<AppEvent>> useAction)
    {
        var events = GetEventsForChannelInRange(channelId, fromUtc, toUtc).ToArray();
        useAction(events.AsSpan());
    }

    public void Event(string label)
        => AddEvent(CreateEvent(label, AppEventType.Event, Constants.ReservedChannels.ChannelNotSpecified_Id, string.Empty));

    public void Event(string label, AppEventType type)
        => AddEvent(CreateEvent(label, type, Constants.ReservedChannels.ChannelNotSpecified_Id, string.Empty));

    public void Event(string label, AppEventType type, string details)
        => AddEvent(CreateEvent(label, type, Constants.ReservedChannels.ChannelNotSpecified_Id, details));

    public void Event(string label, byte channel)
        => AddEvent(CreateEvent(label, AppEventType.Event, channel, string.Empty));

    public void Event(string label, AppEventType type, byte channel)
        => AddEvent(CreateEvent(label, type, channel, string.Empty));

    public void Event(string label, AppEventType type, byte channel, string details)
        => AddEvent(CreateEvent(label, type, channel, details));

    public void ScreenViewed(string screenName)
        => AddEvent(CreateEvent(screenName, AppEventType.ScreenViewed, Constants.ReservedChannels.ChannelNotSpecified_Id, string.Empty));

    public void ScreenViewed(string screenName, string details)
        => AddEvent(CreateEvent(screenName, AppEventType.ScreenViewed, Constants.ReservedChannels.ChannelNotSpecified_Id, details));

    public void ScreenViewed(string screenName, byte channel)
        => AddEvent(CreateEvent(screenName, AppEventType.ScreenViewed, channel, string.Empty));

    public void ScreenViewed(string screenName, byte channel, string details)
        => AddEvent(CreateEvent(screenName, AppEventType.ScreenViewed, channel, details));

    public Snapshot Snapshot()
    {
        return new Snapshot
        {
            Channels = channels.ToList(),
            Metrics = channels.Select(channel => new MetricsSnapshot
            {
                ChannelId = channel.Id,
                Metrics = GetMetricsForChannel(channel.Id).ToList()
            }).ToList(),
            Events = channels.Select(channel => new EventsSnapshot
            {
                ChannelId = channel.Id,
                Events = GetEventsForChannel(channel.Id).ToList()
            }).ToList(),
            AppState = currentAppLifecycleState,
            AppStateChangedUtc = currentAppLifecycleStateChangedUtc
        };
    }

    public static Channel CreateChannel(byte id, string name)
    {
        return new Channel(id, name, Color.OrangeRed);
    }

    private static AppEvent CreateEvent(string label, AppEventType type, byte channel, string details)
    {
        return new AppEvent(label, type, details, DateTime.UtcNow, externalId: null, channel);
    }
}
