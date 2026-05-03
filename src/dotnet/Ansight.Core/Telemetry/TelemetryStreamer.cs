using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.Telemetry;

internal sealed class TelemetryStreamer : IDisposable
{
    private const int MaxMetricsBatchSize = 160;
    private const int MaxPendingMetrics = 2000;
    private const int MaxEventsBatchSize = 160;

    private readonly PairingSessionTransport transport;
    private readonly SemaphoreSlim metricsSignal = new(0);
    private readonly SemaphoreSlim eventsSignal = new(0);
    private readonly Lock metricsLock = new();
    private readonly Lock eventsLock = new();
    private readonly List<Metric> pendingMetrics = [];
    private readonly List<AppEvent> pendingEvents = [];
    private readonly HashSet<byte> announcedMetricChannels = [];
    private readonly HashSet<Guid> pendingEventIds = [];
    private IDataSink? metricsDataSink;
    private IDataSink? eventsDataSink;
    private EventHandler<MetricsUpdatedEventArgs>? metricsUpdatedHandler;
    private EventHandler<AppEventsUpdatedEventArgs>? eventsUpdatedHandler;
    private CancellationTokenSource? metricsPumpCts;
    private CancellationTokenSource? eventsPumpCts;
    private Task? metricsPumpTask;
    private Task? eventsPumpTask;
    private bool disposed;

    public TelemetryStreamer(PairingSessionTransport transport)
    {
        this.transport = transport;
    }

    public async Task<OperationResult> StartAsync(
        IDataSink dataSink,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSink);

        if (!transport.IsOpen)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        await StopAsync(progress: null, CancellationToken.None);

        var channels = dataSink.Channels ?? Array.Empty<Channel>();

        lock (metricsLock)
        {
            pendingMetrics.Clear();
            announcedMetricChannels.Clear();
        }

        lock (eventsLock)
        {
            pendingEvents.Clear();
            pendingEventIds.Clear();
        }

        var channelAnnouncement = await SendMetricChannelDefinitionsAsync(channels, progress, cancellationToken);
        if (!channelAnnouncement.Success)
        {
            return channelAnnouncement;
        }

        lock (metricsLock)
        {
            metricsDataSink = dataSink;
        }

        var seedMetrics = dataSink.Metrics
            .OrderBy(metric => metric.CapturedAtUtc)
            .TakeLast(MaxMetricsBatchSize)
            .ToArray();

        lock (metricsLock)
        {
            pendingMetrics.AddRange(seedMetrics);
        }

        metricsUpdatedHandler = (_, args) =>
        {
            if (args.Added.Count == 0)
            {
                return;
            }

            lock (metricsLock)
            {
                pendingMetrics.AddRange(args.Added);
                if (pendingMetrics.Count > MaxPendingMetrics)
                {
                    pendingMetrics.RemoveRange(0, pendingMetrics.Count - MaxPendingMetrics);
                }
            }

            metricsSignal.Release();
        };

        dataSink.OnMetricsUpdated += metricsUpdatedHandler;
        metricsPumpCts = new CancellationTokenSource();
        metricsPumpTask = Task.Run(() => RunMetricsPumpAsync(progress, metricsPumpCts.Token));

        eventsUpdatedHandler = (_, args) =>
        {
            if (args.Added.Count == 0)
            {
                return;
            }

            var didAddAny = false;

            lock (eventsLock)
            {
                foreach (var @event in args.Added.OrderBy(a => a.CapturedAtUtc))
                {
                    if (!pendingEventIds.Add(@event.Id))
                    {
                        continue;
                    }

                    pendingEvents.Add(@event);
                    didAddAny = true;
                }
            }

            if (didAddAny)
            {
                eventsSignal.Release();
            }
        };

        lock (eventsLock)
        {
            eventsDataSink = dataSink;
        }

        dataSink.OnEventsUpdated += eventsUpdatedHandler;
        eventsPumpCts = new CancellationTokenSource();
        eventsPumpTask = Task.Run(() => RunEventsPumpAsync(progress, eventsPumpCts.Token));

        var seedEvents = dataSink.Events
            .OrderBy(@event => @event.CapturedAtUtc)
            .ToArray();

        var didSeedEvents = false;
        if (seedEvents.Length > 0)
        {
            lock (eventsLock)
            {
                foreach (var @event in seedEvents)
                {
                    if (!pendingEventIds.Add(@event.Id))
                    {
                        continue;
                    }

                    pendingEvents.Add(@event);
                    didSeedEvents = true;
                }
            }
        }

        if (seedMetrics.Length > 0)
        {
            metricsSignal.Release();
        }

        if (didSeedEvents)
        {
            eventsSignal.Release();
        }

        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.Telemetry,
            "Telemetry streaming started.",
            source: HostConnectionSource.Telemetry);
        return OperationResult.FromSuccess("Telemetry streaming started.");
    }

    public async Task<OperationResult> StopAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        IDataSink? dataSink;
        EventHandler<MetricsUpdatedEventArgs>? metricsUpdatedHandler;
        Task? pumpTask;
        CancellationTokenSource? pumpCts;
        IDataSink? eventsDataSink;
        EventHandler<AppEventsUpdatedEventArgs>? eventsUpdatedHandler;
        Task? eventsPumpTask;
        CancellationTokenSource? eventsPumpCts;

        lock (metricsLock)
        {
            dataSink = metricsDataSink;
            metricsUpdatedHandler = this.metricsUpdatedHandler;
            pumpTask = metricsPumpTask;
            pumpCts = metricsPumpCts;
            metricsDataSink = null;
            this.metricsUpdatedHandler = null;
            metricsPumpTask = null;
            metricsPumpCts = null;
            pendingMetrics.Clear();
            announcedMetricChannels.Clear();
        }

        lock (eventsLock)
        {
            eventsDataSink = this.eventsDataSink;
            eventsUpdatedHandler = this.eventsUpdatedHandler;
            eventsPumpTask = this.eventsPumpTask;
            eventsPumpCts = this.eventsPumpCts;
            this.eventsDataSink = null;
            this.eventsUpdatedHandler = null;
            this.eventsPumpTask = null;
            this.eventsPumpCts = null;
            pendingEvents.Clear();
            pendingEventIds.Clear();
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

        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.Telemetry,
            "Telemetry streaming stopped.",
            source: HostConnectionSource.Telemetry);
        return OperationResult.FromSuccess("Telemetry streaming stopped.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        metricsPumpCts?.Cancel();
        eventsPumpCts?.Cancel();

        if (metricsDataSink is not null && metricsUpdatedHandler is not null)
        {
            metricsDataSink.OnMetricsUpdated -= metricsUpdatedHandler;
        }

        if (eventsDataSink is not null && eventsUpdatedHandler is not null)
        {
            eventsDataSink.OnEventsUpdated -= eventsUpdatedHandler;
        }

        metricsPumpCts?.Dispose();
        eventsPumpCts?.Dispose();
        metricsSignal.Dispose();
        eventsSignal.Dispose();
        metricsDataSink = null;
        metricsUpdatedHandler = null;
        metricsPumpTask = null;
        metricsPumpCts = null;
        eventsDataSink = null;
        eventsUpdatedHandler = null;
        eventsPumpTask = null;
        eventsPumpCts = null;
    }

    private async Task RunMetricsPumpAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await metricsSignal.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                Metric[] batch;
                IDataSink? dataSink;

                lock (metricsLock)
                {
                    if (pendingMetrics.Count == 0)
                    {
                        break;
                    }

                    var batchSize = Math.Min(pendingMetrics.Count, MaxMetricsBatchSize);
                    batch = pendingMetrics.Take(batchSize).ToArray();
                    pendingMetrics.RemoveRange(0, batchSize);
                    dataSink = metricsDataSink;
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
                        HostPairingProgressReporter.Report(
                            progress,
                            HostConnectionProgressKind.Warning,
                            $"Metrics streaming stopped: {channelResult.Message}",
                            source: HostConnectionSource.Telemetry);
                        return;
                    }
                }

                var metricsResult = await SendMetricsBatchAsync(batch, cancellationToken);
                if (!metricsResult.Success)
                {
                    HostPairingProgressReporter.Report(
                        progress,
                        HostConnectionProgressKind.Warning,
                        $"Metrics streaming stopped: {metricsResult.Message}",
                        source: HostConnectionSource.Telemetry);
                    return;
                }
            }
        }
    }

    private async Task RunEventsPumpAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await eventsSignal.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                AppEvent[] batch;

                lock (eventsLock)
                {
                    if (pendingEvents.Count == 0)
                    {
                        break;
                    }

                    var batchSize = Math.Min(pendingEvents.Count, MaxEventsBatchSize);
                    batch = pendingEvents.Take(batchSize).ToArray();
                }

                if (batch.Length == 0)
                {
                    break;
                }

                var eventsResult = await SendEventsBatchAsync(batch, progress, cancellationToken);
                if (!eventsResult.Success)
                {
                    HostPairingProgressReporter.Report(
                        progress,
                        HostConnectionProgressKind.Warning,
                        $"Events streaming stopped: {eventsResult.Message}",
                        source: HostConnectionSource.Telemetry);
                    return;
                }

                lock (eventsLock)
                {
                    var removeCount = Math.Min(batch.Length, pendingEvents.Count);
                    for (var i = 0; i < removeCount; i++)
                    {
                        pendingEventIds.Remove(pendingEvents[i].Id);
                    }

                    pendingEvents.RemoveRange(0, removeCount);
                }
            }
        }
    }

    private async Task<OperationResult> SendMetricChannelDefinitionsAsync(
        IReadOnlyList<Channel> channels,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (channels.Count == 0)
        {
            return OperationResult.FromSuccess("No metric channels to stream.");
        }

        Channel[] newChannels;
        lock (metricsLock)
        {
            newChannels = channels
                .Where(channel => announcedMetricChannels.Add(channel.Id))
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

        var sendResult = await transport.SendTextAsync(payload, cancellationToken);
        if (!sendResult.Success)
        {
            return sendResult;
        }

        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.Telemetry,
            $"WS -> announced {newChannels.Length} metric channels",
            isVerbose: true,
            source: HostConnectionSource.Telemetry);
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

        return await transport.SendTextAsync(payload, cancellationToken);
    }

    private Task<OperationResult> SendEventsBatchAsync(
        IReadOnlyList<AppEvent> events,
        IProgress<HostConnectionProgressUpdate>? progress,
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

        var sendTask = transport.SendTextAsync(payload, cancellationToken);
        if (progress is not null)
        {
            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.Telemetry,
                $"WS -> streamed {events.Count} events",
                isVerbose: true,
                source: HostConnectionSource.Telemetry);
        }

        return sendTask;
    }

    private static string ToColorHex(System.Drawing.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
