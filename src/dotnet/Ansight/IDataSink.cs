namespace Ansight;

/// <summary>
/// Contract for storing and querying Ansight's metrics/events and notifying listeners about updates.
/// </summary>
public interface IDataSink
{
    /// <summary>
    /// All channels currently tracked by the sink.
    /// </summary>
    public IReadOnlyList<Channel> Channels { get; }
    
    /// <summary>
    /// Occurs when a new set of metrics is added to the data sink or existing values are removed or both.
    /// </summary>
    public event EventHandler<MetricsUpdatedEventArgs>? OnMetricsUpdated;
    
    /// <summary>
    /// Occurs when a new set of events are added or existing values are removed or both.
    /// </summary>
    public event EventHandler<AppEventsUpdatedEventArgs>? OnEventsUpdated;
    
    /// <summary>
    /// Returns all metrics across all channels.
    /// </summary>
    IReadOnlyList<Metric> Metrics { get; }
    
    /// <summary>
    /// Returns all events across all channels.
    /// </summary>
    IReadOnlyList<AppEvent> Events { get; }
    
    /// <summary>
    /// Gets all metrics for the given channel.
    /// </summary>
    IReadOnlyList<Metric> GetMetricsForChannel(Channel channel);
    
    /// <summary>
    /// Gets all metrics for the given channel ID.
    /// </summary>
    IReadOnlyList<Metric> GetMetricsForChannel(byte channelId);
    
    /// <summary>
    /// Gets metrics for a channel within a UTC time window.
    /// </summary>
    IReadOnlyList<Metric> GetMetricsForChannelInRange(Channel channel, DateTime fromUtc, DateTime toUtc);
    
    /// <summary>
    /// Gets metrics for a channel ID within a UTC time window.
    /// </summary>
    IReadOnlyList<Metric> GetMetricsForChannelInRange(byte channelId, DateTime fromUtc, DateTime toUtc);
    
    /// <summary>
    /// Returns a span descriptor for metrics in a given range.
    /// </summary>
    ChannelSpan GetMetricsChannelSpanForRange(byte channelId, DateTime fromUtc, DateTime toUtc);
    
    /// <summary>
    /// Executes a consumer against metrics in a given range without extra allocations.
    /// </summary>
    void UseMetricsInChannelForRange(byte channelId, DateTime fromUtc, DateTime toUtc, Action<ReadOnlySpan<Metric>> useAction);
    
    /// <summary>
    /// Gets all events for the given channel.
    /// </summary>
    IReadOnlyList<AppEvent> GetEventsForChannel(Channel channel);
    
    /// <summary>
    /// Gets all events for the given channel ID.
    /// </summary>
    IReadOnlyList<AppEvent> GetEventsForChannel(byte channelId);
    
    /// <summary>
    /// Gets events for a channel within a UTC time window.
    /// </summary>
    IReadOnlyList<AppEvent> GetEventsForChannelInRange(Channel channel, DateTime fromUtc, DateTime toUtc);
    
    /// <summary>
    /// Gets events for a channel ID within a UTC time window.
    /// </summary>
    IReadOnlyList<AppEvent> GetEventsForChannelInRange(byte channelId, DateTime fromUtc, DateTime toUtc);
    
    /// <summary>
    /// Executes a consumer against events in a given range without extra allocations.
    /// </summary>
    void UseEventsInChannelForRange(byte channelId, DateTime fromUtc, DateTime toUtc, Action<ReadOnlySpan<AppEvent>> useAction);

    /// <summary>
    /// Records an event on the unspecified channel using the default event type.
    /// </summary>
    void Event(string label);

    /// <summary>
    /// Records an event on the unspecified channel with a specific event type.
    /// </summary>
    void Event(string label, AppEventType type);

    /// <summary>
    /// Records an event on the unspecified channel with a specific event type and details.
    /// </summary>
    void Event(string label, AppEventType type, string details);

    /// <summary>
    /// Records an event for the given channel using the default event type.
    /// </summary>
    void Event(string label, byte channel);

    /// <summary>
    /// Records an event for the given channel with a specific event type.
    /// </summary>
    void Event(string label, AppEventType type, byte channel);

    /// <summary>
    /// Records an event for the given channel with a specific event type and details.
    /// </summary>
    void Event(string label, AppEventType type, byte channel, string details);

    /// <summary>
    /// Creates a full copy of the data currently in the data sink for export.
    /// </summary>
    Snapshot Snapshot();
}
