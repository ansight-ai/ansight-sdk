namespace Ansight.Telemetry.Snapshots;

public class Snapshot
{
    public List<Channel>? Channels { get; set; }
    
    public List<MetricsSnapshot>? Metrics { get; set; }
    
    public List<EventsSnapshot>? Events { get; set; }

    public AppLifecycleState AppState { get; set; } = AppLifecycleState.Unknown;

    public DateTimeOffset? AppStateChangedUtc { get; set; }

}

public class MetricsSnapshot
{
    public byte ChannelId { get; set; }
    
    public List<Metric>? Metrics { get; set; }
}


public class EventsSnapshot
{
    public byte ChannelId { get; set; }
    
    public List<AppEvent>? Events { get; set; }

}
