using System.Drawing;
using System.Runtime.InteropServices;

namespace Ansight;

internal class MutableDataSink : IDataSink
{
    private static readonly Comparer<AppEvent> EventCapturedAtComparer =
        Comparer<AppEvent>.Create((a, b) => a.CapturedAtUtc.CompareTo(b.CapturedAtUtc));
    
    private static readonly Comparer<Metric> MetricCapturedAtComparer =
        Comparer<Metric>.Create((a, b) => a.CapturedAtUtc.CompareTo(b.CapturedAtUtc));
    
    private readonly Options options;

    public MutableDataSink(Options  options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        
        this.options = options;

        options.Validate();

        if (options.DefaultMemoryChannels.HasFlag(DefaultMemoryChannels.ManagedHeap))
        {
            channels[Constants.ReservedChannels.ClrMemoryUsage_Id] = new Channel(Constants.ReservedChannels.ClrMemoryUsage_Id, Constants.ReservedChannels.ClrMemoryUsage_Name, Constants.ReservedChannels.ClrMemoryUsage_Color);
        }
        channels[Constants.ReservedChannels.FramesPerSecond_Id] = new Channel(Constants.ReservedChannels.FramesPerSecond_Id, Constants.ReservedChannels.FramesPerSecond_Name, Constants.ReservedChannels.FramesPerSecond_Color);
        
#if IOS || MACCATALYST
        if (options.DefaultMemoryChannels.HasFlag(DefaultMemoryChannels.PhysicalFootprint))
        {
            channels[Constants.ReservedChannels.PlatformMemoryUsage_Id] = new Channel(Constants.ReservedChannels.PlatformMemoryUsage_Id, Constants.ReservedChannels.PlatformMemoryUsage_Name, Constants.ReservedChannels.PlatformMemoryUsage_Color);
        }
#elif ANDROID
        if (options.DefaultMemoryChannels.HasFlag(DefaultMemoryChannels.NativeHeap))
        {
            channels[Constants.ReservedChannels.NativeHeapAllocated_Id] = new Channel(Constants.ReservedChannels.NativeHeapAllocated_Id, Constants.ReservedChannels.NativeHeapAllocated_Name, Constants.ReservedChannels.NativeHeapAllocated_Color);
        }

        if (options.DefaultMemoryChannels.HasFlag(DefaultMemoryChannels.ResidentSetSize))
        {
            channels[Constants.ReservedChannels.Rss_Id] = new Channel(Constants.ReservedChannels.Rss_Id, Constants.ReservedChannels.Rss_Name, Constants.ReservedChannels.Rss_Color);
        }
#endif
        
        channels[Constants.ReservedChannels.ChannelNotSpecified_Id] = new Channel(Constants.ReservedChannels.ChannelNotSpecified_Id, "Not Specified", default(Color));

        if (options.AdditionalChannels != null 
            && options.AdditionalChannels.Count > 0)
        {
            foreach (var channel in options.AdditionalChannels)
            {
                channels[channel.Id] = channel;
            }
        }

        foreach (var channel in channels)
        {
            metricsByChannel[channel.Key] = new List<Metric>(options.MaximumBufferSize);
            eventsByChannel[channel.Key] = new List<AppEvent>(options.MaximumBufferSize);
        }
    }
    
    
    private readonly Lock channelLock = new Lock();
    private readonly Dictionary<byte, Channel> channels = new Dictionary<byte, Channel>();
    
    private readonly Lock metricsLock = new Lock();
    private DateTime minMetricsDateTime = DateTime.MaxValue;
    private DateTime maxMetricsDateTime = DateTime.MinValue;
    private readonly Dictionary<byte, List<Metric>> metricsByChannel = new Dictionary<byte, List<Metric>>();
    
    private readonly Lock eventsLock = new Lock();
    private DateTime minEventsDateTime = DateTime.MaxValue;
    private DateTime maxEventsDateTime = DateTime.MinValue;
    private readonly Dictionary<byte, List<AppEvent>> eventsByChannel = new Dictionary<byte, List<AppEvent>>();

    public event EventHandler<MetricsUpdatedEventArgs>? OnMetricsUpdated;
    public event EventHandler<AppEventsUpdatedEventArgs>? OnEventsUpdated;
    
    public IReadOnlyList<Channel> Channels
    {
        get
        {
            lock (channelLock)
            {
                // Intentional 'ToList' to not pass around a reference to a thread safe value.
                return channels.Values.ToList();
            }
        }
    }
    
    public IReadOnlyList<Metric> Metrics
    {
        get
        {
            lock (metricsLock)
            {
                var metricsCount = metricsByChannel.Sum(m => m.Value.Count);
                var metrics = new List<Metric>(metricsCount);

                foreach (var channelValues in metricsByChannel.Values)
                {
                    metrics.AddRange(channelValues);
                }
                
                metrics.Sort();

                return metrics;
            }
        }
    }
    
    public IReadOnlyList<AppEvent> Events
    {
        get
        {
            lock (eventsLock)
            {
                var eventsCount = eventsByChannel.Sum(m => m.Value.Count);
                var events = new List<AppEvent>(eventsCount);

                foreach (var channelValues in eventsByChannel.Values)
                {
                    events.AddRange(channelValues);
                }

                return events;
            }
        }
    }
    
    public IReadOnlyList<Metric> GetMetricsForChannel(Channel channel)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));
        
        return GetMetricsForChannel(channel.Id);
    }

    public IReadOnlyList<Metric> GetMetricsForChannel(byte channelId)
    {
        lock (metricsLock)
        {
            if (!this.metricsByChannel.TryGetValue(channelId, out var channelData))
            {
                return  Array.Empty<Metric>();
            }

            // Intentional 'ToList' to not pass around a reference to a thread safe value.
            return channelData.ToList();
        }
    }

    public IReadOnlyList<Metric> GetMetricsForChannelInRange(Channel channel, DateTime fromUtc, DateTime toUtc)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));
        
        return GetMetricsForChannelInRange(channel.Id, fromUtc, toUtc);
    }

    public IReadOnlyList<Metric> GetMetricsForChannelInRange(byte channelId, DateTime fromUtc, DateTime toUtc)
    {
        if (fromUtc > toUtc)
        {
            fromUtc = toUtc;
        }
        
        lock (metricsLock)
        {
            if (!this.metricsByChannel.TryGetValue(channelId, out var channelData))
            {
                return  Array.Empty<Metric>();
            }
            
            var isBeforeBounds = toUtc < this.minMetricsDateTime;
            var isAfterBounds = fromUtc > this.maxMetricsDateTime;

            if (isBeforeBounds || isAfterBounds)
            {
                return Array.Empty<Metric>();
            }

            if (fromUtc < this.minMetricsDateTime
                && toUtc > this.maxMetricsDateTime)
            {
                // Intentional by-reference copy of inner values
                return channelData.ToList();
            }
            
            var size = CalculateBufferSize(fromUtc, toUtc);

            List<Metric> metrics = new List<Metric>(size);


            foreach (var metric in channelData)
            {
                if (metric.CapturedAtUtc >= fromUtc && metric.CapturedAtUtc <= toUtc)
                {
                    metrics.Add(metric);
                }

                if (metric.CapturedAtUtc > toUtc)
                {
                    break;
                }
            }
            
            return metrics;
        }
    }

    public ChannelSpan GetMetricsChannelSpanForRange(byte channelId, DateTime fromUtc, DateTime toUtc)
    {
        if (fromUtc > toUtc)
        {
            fromUtc = toUtc;
        }
        
        lock (metricsLock)
        {
            if (!this.metricsByChannel.TryGetValue(channelId, out var channelData))
            {
                return ChannelSpan.Invalid;
            }
            
            var isBeforeBounds = toUtc < this.minMetricsDateTime;
            var isAfterBounds = fromUtc > this.maxMetricsDateTime;

            if (isBeforeBounds || isAfterBounds)
            {
                return ChannelSpan.Invalid;
            }

            var count = 0;
            var minValue = long.MaxValue;
            var maxValue = long.MinValue;

            foreach (var metric in channelData)
            {
                if (metric.CapturedAtUtc >= fromUtc 
                    && metric.CapturedAtUtc <= toUtc)
                {
                    count++;
                    if (metric.Value < minValue)
                    {
                        minValue = metric.Value;
                    }

                    if (metric.Value > maxValue)
                    {
                        maxValue = metric.Value;
                    }
                }

                if (metric.CapturedAtUtc > toUtc)
                {
                    break;
                }
            }
            
            return new ChannelSpan()
            {
                ChannelId = channelId,
                MinValue = minValue,
                MaxValue = maxValue,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Count = count,
                Valid = true
            };
        }
    }

    public void UseMetricsInChannelForRange(byte channelId, DateTime fromUtc, DateTime toUtc, Action<ReadOnlySpan<Metric>> useAction)
    {
        if (useAction == null) throw new ArgumentNullException(nameof(useAction));

        if (fromUtc > toUtc)
        {
            fromUtc = toUtc;
        }

        lock (metricsLock)
        {
            if (!this.metricsByChannel.TryGetValue(channelId, out var channelData))
            {
                // Invalid or unknown channel, discard request.
                return;
            }

            if (channelData.Count == 0)
            {
                // no channel content, do nothing.
                return;
            }

            if (toUtc < this.minMetricsDateTime
                || fromUtc > this.maxMetricsDateTime)
            {
                // Invalid span range, discard request.
                return;
            }

            var start = 0;
            var end = channelData.Count - 1;

            if (fromUtc > minMetricsDateTime)
            {
                start = BinarySearchHelper.FindFirstIndex(channelData, fromUtc);
            }
            
            if (toUtc < maxMetricsDateTime)
            {
                end = BinarySearchHelper.FindLastIndex(channelData, toUtc);
            }

            var length = Math.Max(0, end - start + 1);
            if (length <= 0)
            {
                return;
            }

            ReadOnlySpan<Metric> range = CollectionsMarshal.AsSpan(channelData)
                .Slice(start, length);
            
            useAction(range);
        }
    }

    private int CalculateBufferSize(DateTime fromUtc, DateTime toUtc)
    {
        var elapsed = toUtc - fromUtc;
        var estimatedCount = (int)(elapsed.TotalSeconds * (int)Math.Ceiling(1000f / options.SampleFrequencyMilliseconds));

        if (estimatedCount > options.MaximumBufferSize)
        {
            return options.MaximumBufferSize;
        }

        return estimatedCount;
    }

    public IReadOnlyList<AppEvent> GetEventsForChannel(Channel channel)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));
        
        return GetEventsForChannel(channel.Id);
    }

    public IReadOnlyList<AppEvent> GetEventsForChannel(byte channelId)
    {
        lock (eventsLock)
        {
            if (!this.eventsByChannel.TryGetValue(channelId, out var channelData))
            {
                return  Array.Empty<AppEvent>();
            }

            // Intentional 'ToList' to not pass around a reference to a thread safe value.
            return channelData.ToList();
        }
    }

    public IReadOnlyList<AppEvent> GetEventsForChannelInRange(Channel channel, DateTime fromUtc, DateTime toUtc)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));
        
        return GetEventsForChannelInRange(channel.Id, fromUtc, toUtc);
    }

    public IReadOnlyList<AppEvent> GetEventsForChannelInRange(byte channelId, DateTime fromUtc, DateTime toUtc)
    {
        if (fromUtc > toUtc)
        {
            fromUtc = toUtc;
        }
        
        lock (eventsLock)
        {
            if (!this.eventsByChannel.TryGetValue(channelId, out var channelData))
            {
                return  Array.Empty<AppEvent>();
            }
            
            var isBeforeBounds = toUtc < this.minMetricsDateTime;
            var isAfterBounds = fromUtc > this.maxMetricsDateTime;

            if (isBeforeBounds || isAfterBounds)
            {
                return Array.Empty<AppEvent>();
            }

            if (fromUtc < this.minMetricsDateTime
                && toUtc > this.maxMetricsDateTime)
            {
                // Intentional 'ToList' to not pass around a reference to a thread safe value.
                return channelData.ToList();
            }
            
            var size = CalculateBufferSize(fromUtc, toUtc);

            var values = new List<AppEvent>(size);

            foreach (var datum in channelData)
            {
                if (datum.CapturedAtUtc >= fromUtc && datum.CapturedAtUtc <= toUtc)
                {
                    values.Add(datum);
                }

                if (datum.CapturedAtUtc > toUtc)
                {
                    break;
                }
            }
            
            return values;
        }
    }

    public void UseEventsInChannelForRange(byte channelId, 
                                           DateTime fromUtc,
                                           DateTime toUtc,
                                           Action<ReadOnlySpan<AppEvent>> useAction)
    {
        if (useAction == null) throw new ArgumentNullException(nameof(useAction));

        if (fromUtc > toUtc)
        {
            fromUtc = toUtc;
        }

        lock (eventsLock)
        {
            if (!this.eventsByChannel.TryGetValue(channelId, out var channelData))
            {
                // Invalid or unknown channel, discard request.
                return;
            }

            if (channelData.Count == 0)
            {
                // no channel content, do nothing.
                return;
            }

            if (toUtc < this.minEventsDateTime
                || fromUtc > this.maxEventsDateTime)
            {
                // Invalid span range, discard request.
                return;
            }

            var start = 0;
            var end = channelData.Count - 1;

            if (fromUtc > minEventsDateTime)
            {
                start = BinarySearchHelper.FindFirstIndex(channelData, fromUtc);
            }
            
            if (toUtc < maxEventsDateTime)
            {
                end = BinarySearchHelper.FindLastIndex(channelData, toUtc);
            }
            
            var length = Math.Max(0, end - start + 1);
            if (length == 0)
            {
                return;
            }

            ReadOnlySpan<AppEvent> range = CollectionsMarshal.AsSpan(channelData)
                                                                  .Slice(start, length);
            
            useAction(range);
        }
    }

    public void RecordMemorySnapshot(MemorySnapshot snapshot)
    {
        var clr = snapshot.ManagedHeapBytes;
        MutateMetrics(metrics =>
        {
            List<Metric> added = new List<Metric>();
            if (metrics.TryGetValue(Constants.ReservedChannels.ClrMemoryUsage_Id, out var clrMetrics))
            {
                var metric = new Metric()
                {
                    CapturedAtUtc = DateTime.UtcNow,
                    Channel = Constants.ReservedChannels.ClrMemoryUsage_Id,
                    Value = clr
                };
                
                added.Add(metric);
                clrMetrics.Add(metric);
            }
            
#if IOS || MACCATALYST
            var platform = snapshot.RssBytes;
            if (metrics.TryGetValue(Constants.ReservedChannels.PlatformMemoryUsage_Id, 
                                    out var platformMetrics))
            {
                var metric = new Metric()
                {
                    CapturedAtUtc = DateTime.UtcNow,
                    Channel = Constants.ReservedChannels.PlatformMemoryUsage_Id,
                    Value = platform
                };
                
                added.Add(metric);
                platformMetrics.Add(metric);
            }
#elif ANDROID
            AddMetricIfPresent(metrics, added, Constants.ReservedChannels.NativeHeapAllocated_Id, snapshot.NativeHeapAllocatedBytes);
            AddMetricIfPresent(metrics, added, Constants.ReservedChannels.Rss_Id, snapshot.RssBytes);
#endif

            return added;
        });
    }

#if ANDROID
    private static void AddMetricIfPresent(Dictionary<byte, List<Metric>> metrics, List<Metric> added, byte channelId, long value)
    {
        if (!metrics.TryGetValue(channelId, out var channelMetrics))
        {
            return;
        }

        var metric = new Metric()
        {
            CapturedAtUtc = DateTime.UtcNow,
            Channel = channelId,
            Value = value
        };

        added.Add(metric);
        channelMetrics.Add(metric);
    }
#endif
    
    public void Metric(long value, byte channel)
    {
        MutateMetrics(metrics =>
        {
            if (!metrics.TryGetValue(channel, out var channelMetrics))
            {
                return Array.Empty<Metric>();
            }

            var metric = new Metric()
            {
                CapturedAtUtc = DateTime.UtcNow,
                Channel = channel,
                Value = value
            };
            
            channelMetrics.Add(metric);
            return new List<Metric>()
            {
                metric
            };
        });
    }

    private void MutateMetrics(Func<Dictionary<byte, List<Metric>>, IReadOnlyList<Metric>> mutator)
    {
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));

        IReadOnlyList<Metric>? newValues = null;
        List<Metric>? trimmedValues = null;
        
        var expiryTime = DateTime.UtcNow - TimeSpan.FromSeconds(options.RetentionPeriodSeconds);
        
        lock (metricsLock)
        {
            newValues = mutator(metricsByChannel);

            foreach (var channel in metricsByChannel.Values)
            {
                var removeCount = 0;
                var initialCount = channel.Count;

                while (removeCount < initialCount && channel[removeCount].CapturedAtUtc < expiryTime)
                {
                    removeCount++;
                }

                if (removeCount > 0)
                {
                    trimmedValues ??= new List<Metric>();
                    trimmedValues.AddRange(channel.GetRange(0, removeCount));
                    channel.RemoveRange(0, removeCount);
                }
                
                if (channel.Count > 0)
                {
                    var last = channel[^1];
                    var first = channel[0];

                    if (last.CapturedAtUtc > maxMetricsDateTime)
                    {
                        maxMetricsDateTime = last.CapturedAtUtc;
                    }
                    
                    if (first.CapturedAtUtc < minMetricsDateTime)
                    {
                        minMetricsDateTime = first.CapturedAtUtc;
                    }
                }
            }
        }
        
        var didChange = (newValues != null && newValues.Count > 0)
                        || (trimmedValues != null && trimmedValues.Count > 0);

        if (didChange)
        {
            IReadOnlyList<Metric> added = newValues ?? Array.Empty<Metric>();
            IReadOnlyList<Metric> removed = trimmedValues as IReadOnlyList<Metric> ?? Array.Empty<Metric>();
            
            this.OnMetricsUpdated?.Invoke(this, new MetricsUpdatedEventArgs(added, removed));
        }
    }

    public void Event(string label)
    {
        Event(label, Constants.DefaultEventType, Constants.ReservedChannels.ChannelNotSpecified_Id, string.Empty);
    }

    public void Event(string label, AppEventType type)
    {
        Event(label, type, Constants.ReservedChannels.ChannelNotSpecified_Id, string.Empty);
    }

    public void Event(string label, AppEventType type, string details)
    {
        Event(label, type, Constants.ReservedChannels.ChannelNotSpecified_Id, details);
    }
    
    public void Event(string label, byte channel)
    {
        Event(label, Constants.DefaultEventType, channel, string.Empty);
    }

    public void Event(string label, AppEventType type, byte channel)
    {
        Event(label, type, channel, string.Empty);
    }

    public void Event(string label, AppEventType type, byte channel, string details)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        details ??= string.Empty;
        
        MutateEvents(metrics =>
        {
            if (!metrics.TryGetValue(channel, out var channelEvents))
            {
                return Array.Empty<AppEvent>();
            }

            var @event = new AppEvent(label, type, details, DateTime.UtcNow, externalId: null, channel); 
            
            channelEvents.Add(@event);

            return new List<AppEvent>()
            {
                @event
            };
        });
    }

    public Snapshot Snapshot()
    {
        var snapshot = new Snapshot();
        
        lock (channelLock)
        {
            snapshot.Channels = channels.Values.ToList();
        }

        lock (metricsLock)
        {
            var channelSnapshots = new List<MetricsSnapshot>(metricsByChannel.Count);

            foreach (var channel in metricsByChannel)
            {
                channelSnapshots.Add(new MetricsSnapshot()
                {
                    ChannelId = channel.Key,
                    Metrics = channel.Value.ToList()
                });
            }

            snapshot.Metrics = channelSnapshots;
        }
        
        lock (eventsLock)
        {
            var channelSnapshots = new List<EventsSnapshot>(eventsByChannel.Count);

            foreach (var channel in eventsByChannel)
            {
                channelSnapshots.Add(new EventsSnapshot()
                {
                    ChannelId = channel.Key,
                    Events = channel.Value.ToList()
                });
            }

            snapshot.Events = channelSnapshots;
        }

        return snapshot;
    }


    private void MutateEvents(Func<Dictionary<byte, List<AppEvent>>, IReadOnlyList<AppEvent>> mutator)
    {
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));

        IReadOnlyList<AppEvent>? newValues = null;
        List<AppEvent>? trimmedValues = null;
        
        var expiryTime = DateTime.UtcNow - TimeSpan.FromSeconds(options.RetentionPeriodSeconds);
        
        lock (eventsLock)
        {
            newValues = mutator(eventsByChannel);

            foreach (var channel in eventsByChannel.Values)
            {
                var removeCount = 0;
                var initialCount = channel.Count;

                while (removeCount < initialCount && channel[removeCount].CapturedAtUtc < expiryTime)
                {
                    removeCount++;
                }

                if (removeCount > 0)
                {
                    trimmedValues ??= new List<AppEvent>();
                    trimmedValues.AddRange(channel.GetRange(0, removeCount));
                    channel.RemoveRange(0, removeCount);
                }
                
                if (channel.Count > 0)
                {
                    var last = channel[^1];
                    var first = channel[0];

                    if (last.CapturedAtUtc > maxEventsDateTime)
                    {
                        maxEventsDateTime = last.CapturedAtUtc;
                    }
                    
                    if (first.CapturedAtUtc < minEventsDateTime)
                    {
                        minEventsDateTime = first.CapturedAtUtc;
                    }
                }
            }
        }
        
        var didChange = (newValues != null && newValues.Count > 0)
            || (trimmedValues != null && trimmedValues.Count > 0);

        if (didChange)
        {
            IReadOnlyList<AppEvent> added = newValues ?? Array.Empty<AppEvent>();
            IReadOnlyList<AppEvent> removed = trimmedValues as IReadOnlyList<AppEvent> ?? Array.Empty<AppEvent>();
            
            this.OnEventsUpdated?.Invoke(this, new AppEventsUpdatedEventArgs(added, removed));
        }
    }

    public void Clear()
    {
        List<Metric> removedMetrics;
        List<AppEvent> removedEvents;

        lock (metricsLock)
        {
            removedMetrics = metricsByChannel.Values.SelectMany(m => m).ToList();
            foreach (var list in metricsByChannel.Values)
            {
                list.Clear();
            }

            minMetricsDateTime = DateTime.MaxValue;
            maxMetricsDateTime = DateTime.MinValue;
        }

        lock (eventsLock)
        {
            removedEvents = eventsByChannel.Values.SelectMany(e => e).ToList();
            foreach (var list in eventsByChannel.Values)
            {
                list.Clear();
            }

            minEventsDateTime = DateTime.MaxValue;
            maxEventsDateTime = DateTime.MinValue;
        }

        if (removedMetrics.Count > 0)
        {
            OnMetricsUpdated?.Invoke(this, new MetricsUpdatedEventArgs(Array.Empty<Metric>(), removedMetrics));
        }

        if (removedEvents.Count > 0)
        {
            OnEventsUpdated?.Invoke(this, new AppEventsUpdatedEventArgs(Array.Empty<AppEvent>(), removedEvents));
        }
    }
}
