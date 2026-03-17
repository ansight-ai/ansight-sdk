using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingTelemetryStreamer : IDisposable
{
    private const int MaxMetricsBatchSize = 160;
    private const int MaxPendingMetrics = 2000;
    private const int MaxEventsBatchSize = 160;

    private readonly PairingSessionTransport _transport;
    private readonly SemaphoreSlim _metricsSignal = new(0);
    private readonly SemaphoreSlim _eventsSignal = new(0);
    private readonly Lock _metricsLock = new();
    private readonly Lock _eventsLock = new();
    private readonly List<Metric> _pendingMetrics = [];
    private readonly List<AppEvent> _pendingEvents = [];
    private readonly HashSet<byte> _announcedMetricChannels = [];
    private readonly HashSet<Guid> _pendingEventIds = [];
    private IDataSink? _metricsDataSink;
    private IDataSink? _eventsDataSink;
    private EventHandler<MetricsUpdatedEventArgs>? _metricsUpdatedHandler;
    private EventHandler<AppEventsUpdatedEventArgs>? _eventsUpdatedHandler;
    private CancellationTokenSource? _metricsPumpCts;
    private CancellationTokenSource? _eventsPumpCts;
    private Task? _metricsPumpTask;
    private Task? _eventsPumpTask;
    private bool _disposed;

    public PairingTelemetryStreamer(PairingSessionTransport transport)
    {
        _transport = transport;
    }

    public async Task<OperationResult> StartAsync(
        IDataSink dataSink,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSink);

        if (!_transport.IsOpen)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        await StopAsync(progress: null, CancellationToken.None);

        var channels = dataSink.Channels ?? Array.Empty<Channel>();

        lock (_metricsLock)
        {
            _pendingMetrics.Clear();
            _announcedMetricChannels.Clear();
        }

        lock (_eventsLock)
        {
            _pendingEvents.Clear();
            _pendingEventIds.Clear();
        }

        var channelAnnouncement = await SendMetricChannelDefinitionsAsync(channels, progress, cancellationToken);
        if (!channelAnnouncement.Success)
        {
            return channelAnnouncement;
        }

        lock (_metricsLock)
        {
            _metricsDataSink = dataSink;
        }

        var seedMetrics = dataSink.Metrics
            .OrderBy(metric => metric.CapturedAtUtc)
            .TakeLast(MaxMetricsBatchSize)
            .ToArray();

        lock (_metricsLock)
        {
            _pendingMetrics.AddRange(seedMetrics);
        }

        _metricsUpdatedHandler = (_, args) =>
        {
            if (args.Added.Count == 0)
            {
                return;
            }

            lock (_metricsLock)
            {
                _pendingMetrics.AddRange(args.Added);
                if (_pendingMetrics.Count > MaxPendingMetrics)
                {
                    _pendingMetrics.RemoveRange(0, _pendingMetrics.Count - MaxPendingMetrics);
                }
            }

            _metricsSignal.Release();
        };

        dataSink.OnMetricsUpdated += _metricsUpdatedHandler;
        _metricsPumpCts = new CancellationTokenSource();
        _metricsPumpTask = Task.Run(() => RunMetricsPumpAsync(progress, _metricsPumpCts.Token));

        _eventsUpdatedHandler = (_, args) =>
        {
            if (args.Added.Count == 0)
            {
                return;
            }

            var didAddAny = false;

            lock (_eventsLock)
            {
                foreach (var @event in args.Added.OrderBy(a => a.CapturedAtUtc))
                {
                    if (!_pendingEventIds.Add(@event.Id))
                    {
                        continue;
                    }

                    _pendingEvents.Add(@event);
                    didAddAny = true;
                }
            }

            if (didAddAny)
            {
                _eventsSignal.Release();
            }
        };

        lock (_eventsLock)
        {
            _eventsDataSink = dataSink;
        }

        dataSink.OnEventsUpdated += _eventsUpdatedHandler;
        _eventsPumpCts = new CancellationTokenSource();
        _eventsPumpTask = Task.Run(() => RunEventsPumpAsync(progress, _eventsPumpCts.Token));

        var seedEvents = dataSink.Events
            .OrderBy(@event => @event.CapturedAtUtc)
            .ToArray();

        var didSeedEvents = false;
        if (seedEvents.Length > 0)
        {
            lock (_eventsLock)
            {
                foreach (var @event in seedEvents)
                {
                    if (!_pendingEventIds.Add(@event.Id))
                    {
                        continue;
                    }

                    _pendingEvents.Add(@event);
                    didSeedEvents = true;
                }
            }
        }

        if (seedMetrics.Length > 0)
        {
            _metricsSignal.Release();
        }

        if (didSeedEvents)
        {
            _eventsSignal.Release();
        }

        progress?.Report("Telemetry streaming started.");
        return OperationResult.FromSuccess("Telemetry streaming started.");
    }

    public async Task<OperationResult> StopAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        IDataSink? dataSink;
        EventHandler<MetricsUpdatedEventArgs>? metricsUpdatedHandler;
        Task? pumpTask;
        CancellationTokenSource? pumpCts;
        IDataSink? eventsDataSink;
        EventHandler<AppEventsUpdatedEventArgs>? eventsUpdatedHandler;
        Task? eventsPumpTask;
        CancellationTokenSource? eventsPumpCts;

        lock (_metricsLock)
        {
            dataSink = _metricsDataSink;
            metricsUpdatedHandler = _metricsUpdatedHandler;
            pumpTask = _metricsPumpTask;
            pumpCts = _metricsPumpCts;
            _metricsDataSink = null;
            _metricsUpdatedHandler = null;
            _metricsPumpTask = null;
            _metricsPumpCts = null;
            _pendingMetrics.Clear();
            _announcedMetricChannels.Clear();
        }

        lock (_eventsLock)
        {
            eventsDataSink = _eventsDataSink;
            eventsUpdatedHandler = _eventsUpdatedHandler;
            eventsPumpTask = _eventsPumpTask;
            eventsPumpCts = _eventsPumpCts;
            _eventsDataSink = null;
            _eventsUpdatedHandler = null;
            _eventsPumpTask = null;
            _eventsPumpCts = null;
            _pendingEvents.Clear();
            _pendingEventIds.Clear();
        }

        if (dataSink is not null && metricsUpdatedHandler is not null)
        {
            dataSink.OnMetricsUpdated -= metricsUpdatedHandler;
        }

        if (eventsDataSink is not null && eventsUpdatedHandler is not null)
        {
            eventsDataSink.OnEventsUpdated -= eventsUpdatedHandler;
        }

        pumpCts?.Cancel();
        eventsPumpCts?.Cancel();

        var currentTaskId = Task.CurrentId;

        if (pumpTask is not null && (!currentTaskId.HasValue || pumpTask.Id != currentTaskId.Value))
        {
            try
            {
                await pumpTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch
            {
                // Ignore pump errors while stopping.
            }
        }

        pumpCts?.Dispose();

        if (eventsPumpTask is not null && (!currentTaskId.HasValue || eventsPumpTask.Id != currentTaskId.Value))
        {
            try
            {
                await eventsPumpTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch
            {
                // Ignore pump errors while stopping.
            }
        }

        eventsPumpCts?.Dispose();

        progress?.Report("Telemetry streaming stopped.");
        return OperationResult.FromSuccess("Telemetry streaming stopped.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _metricsPumpCts?.Cancel();
        _eventsPumpCts?.Cancel();

        if (_metricsDataSink is not null && _metricsUpdatedHandler is not null)
        {
            _metricsDataSink.OnMetricsUpdated -= _metricsUpdatedHandler;
        }

        if (_eventsDataSink is not null && _eventsUpdatedHandler is not null)
        {
            _eventsDataSink.OnEventsUpdated -= _eventsUpdatedHandler;
        }

        _metricsPumpCts?.Dispose();
        _eventsPumpCts?.Dispose();
        _metricsSignal.Dispose();
        _eventsSignal.Dispose();
        _metricsDataSink = null;
        _metricsUpdatedHandler = null;
        _metricsPumpTask = null;
        _metricsPumpCts = null;
        _eventsDataSink = null;
        _eventsUpdatedHandler = null;
        _eventsPumpTask = null;
        _eventsPumpCts = null;
    }

    private async Task RunMetricsPumpAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _metricsSignal.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                Metric[] batch;
                IDataSink? dataSink;

                lock (_metricsLock)
                {
                    if (_pendingMetrics.Count == 0)
                    {
                        break;
                    }

                    var batchSize = Math.Min(_pendingMetrics.Count, MaxMetricsBatchSize);
                    batch = _pendingMetrics.Take(batchSize).ToArray();
                    _pendingMetrics.RemoveRange(0, batchSize);
                    dataSink = _metricsDataSink;
                }

                if (batch.Length == 0)
                {
                    break;
                }

                if (dataSink is not null)
                {
                    var distinctChannelIds = batch.Select(metric => metric.Channel).Distinct().ToHashSet();
                    var channels = dataSink.Channels
                        .Where(channel => distinctChannelIds.Contains(channel.Id))
                        .ToArray();

                    var channelResult = await SendMetricChannelDefinitionsAsync(channels, progress, cancellationToken);
                    if (!channelResult.Success)
                    {
                        progress?.Report($"Metrics streaming stopped: {channelResult.Message}");
                        return;
                    }
                }

                var metricsResult = await SendMetricsBatchAsync(batch, cancellationToken);
                if (!metricsResult.Success)
                {
                    progress?.Report($"Metrics streaming stopped: {metricsResult.Message}");
                    return;
                }
            }
        }
    }

    private async Task RunEventsPumpAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _eventsSignal.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                AppEvent[] batch;

                lock (_eventsLock)
                {
                    if (_pendingEvents.Count == 0)
                    {
                        break;
                    }

                    var batchSize = Math.Min(_pendingEvents.Count, MaxEventsBatchSize);
                    batch = _pendingEvents.Take(batchSize).ToArray();
                }

                if (batch.Length == 0)
                {
                    break;
                }

                var eventsResult = await SendEventsBatchAsync(batch, progress, cancellationToken);
                if (!eventsResult.Success)
                {
                    progress?.Report($"Events streaming stopped: {eventsResult.Message}");
                    return;
                }

                lock (_eventsLock)
                {
                    var removeCount = Math.Min(batch.Length, _pendingEvents.Count);
                    for (var i = 0; i < removeCount; i++)
                    {
                        _pendingEventIds.Remove(_pendingEvents[i].Id);
                    }

                    _pendingEvents.RemoveRange(0, removeCount);
                }
            }
        }
    }

    private async Task<OperationResult> SendMetricChannelDefinitionsAsync(
        IReadOnlyList<Channel> channels,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (channels.Count == 0)
        {
            return OperationResult.FromSuccess("No metric channels to stream.");
        }

        Channel[] newChannels;
        lock (_metricsLock)
        {
            newChannels = channels
                .Where(channel => _announcedMetricChannels.Add(channel.Id))
                .ToArray();
        }

        if (newChannels.Length == 0)
        {
            return OperationResult.FromSuccess("Metric channels already announced.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_METRIC_CHANNELS",
            sentAtUtc = DateTimeOffset.UtcNow,
            channels = newChannels.Select(channel => new
            {
                id = channel.Id,
                name = channel.Name,
                color = ToColorHex(channel.Color)
            }).ToArray()
        }, PairingJson.Compact);

        var sendResult = await _transport.SendTextAsync(payload, cancellationToken);
        if (!sendResult.Success)
        {
            return sendResult;
        }

        progress?.Report($"WS -> announced {newChannels.Length} metric channels");
        return OperationResult.FromSuccess("Metric channel definitions sent.");
    }

    private async Task<OperationResult> SendMetricsBatchAsync(
        IReadOnlyList<Metric> metrics,
        CancellationToken cancellationToken)
    {
        if (metrics.Count == 0)
        {
            return OperationResult.FromSuccess("No metrics to stream.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_METRICS",
            sentAtUtc = DateTimeOffset.UtcNow,
            metrics = metrics.Select(metric => new
            {
                channel = metric.Channel,
                value = metric.Value,
                capturedAtUtc = metric.CapturedAtUtc
            }).ToArray()
        }, PairingJson.Compact);

        return await _transport.SendTextAsync(payload, cancellationToken);
    }

    private Task<OperationResult> SendEventsBatchAsync(
        IReadOnlyList<AppEvent> events,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return Task.FromResult(OperationResult.FromSuccess("No events to stream."));
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_EVENTS",
            sentAtUtc = DateTimeOffset.UtcNow,
            events = events.Select(@event => new
            {
                id = @event.Id,
                label = @event.Label,
                eventType = @event.Type.ToString(),
                details = @event.Details,
                capturedAtUtc = @event.CapturedAtUtc,
                channel = @event.Channel
            }).ToArray()
        }, PairingJson.Compact);

        return _transport.SendRequestAsync(
            payload,
            $"WS -> streamed {events.Count} events",
            "Event batch sent.",
            "Failed to send events",
            progress,
            TimeSpan.FromSeconds(15),
            cancellationToken);
    }

    private static string ToColorHex(System.Drawing.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
