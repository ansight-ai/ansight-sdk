using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ansight;

namespace Ansight.Pairing;

public sealed class PairingSessionClient : IDisposable
{
    private const int MaxMetricsBatchSize = 160;
    private const int MaxPendingMetrics = 2000;
    private const int MaxEventsBatchSize = 160;

    private ClientWebSocket? _webSocket;
    private ConnectResponse? _connectResponse;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
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
    private readonly IPairingHostDiscoveryStrategy? _hostDiscoveryStrategy;

    public PairingSessionClient()
        : this(hostDiscoveryStrategy: null)
    {
    }

    public PairingSessionClient(IPairingHostDiscoveryStrategy? hostDiscoveryStrategy)
    {
        _hostDiscoveryStrategy = hostDiscoveryStrategy;
    }

    public static PairingSessionClientBuilder CreateBuilder() => new();

    public bool TryParseAndValidateDocument(string configJson, string? expectedAppId, out ParsedPairingDocument? document, out string error)
    {
        document = null;

        if (string.IsNullOrWhiteSpace(configJson))
        {
            error = "Paste or load a pairing config.";
            return false;
        }

        if (!TryParseDocument(configJson, out document, out error))
        {
            return false;
        }

        if (document is null)
        {
            error = "Pairing document could not be parsed.";
            return false;
        }

        return TryValidateConfig(document.Config, expectedAppId, out error);
    }

    public bool TryParseAndValidateConfig(string configJson, string? expectedAppId, out PairingConfig? config, out string error)
    {
        config = null;
        if (!TryParseAndValidateDocument(configJson, expectedAppId, out var document, out error))
        {
            return false;
        }

        config = document!.Config;
        return true;
    }

    public bool TryValidateConfig(PairingConfig config, string? expectedAppId, out string error)
    {
        if (!VerifyPairingConfigSignature(config))
        {
            error = "Connection config signature is invalid.";
            return false;
        }

        if (DateTimeOffset.UtcNow > config.ExpiresAt)
        {
            error = $"Connection config expired at {config.ExpiresAt:O}.";
            return false;
        }

        if (!ValidateAppId(config, expectedAppId, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    public async Task<OpenSessionResult> OpenSessionAsync(
        PairingConfig config,
        string clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return await OpenSessionAsync(config, clientName, options: null, progress, cancellationToken);
    }

    public async Task<OpenSessionResult> OpenSessionAsync(
        PairingConfig config,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await CloseSessionAsync(CancellationToken.None);

        progress?.Report($"Config validated. ConfigId: {config.ConfigId}");

        IPAddress? discoveredHostAddress;
        var discoveryMode = options?.DiscoveryMode ?? PairingDiscoveryMode.ConfiguredStrategy;

        if (discoveryMode == PairingDiscoveryMode.BasicManual)
        {
            if (!TryResolveManualHostAddress(options?.ManualHostAddress, out var manualHostAddress))
            {
                return OpenSessionResult.FromFailure("Basic manual discovery requires a valid host IP address.");
            }

            progress?.Report($"Using manual host address: {manualHostAddress}");
            discoveredHostAddress = manualHostAddress!;
        }
        else
        {
            if (_hostDiscoveryStrategy is null)
            {
                return OpenSessionResult.FromFailure("No host discovery strategy was configured for automatic pairing.");
            }

            using var discoverTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            discoverTimeout.CancelAfter(TimeSpan.FromSeconds(8));

            try
            {
                discoveredHostAddress = await _hostDiscoveryStrategy.DiscoverHostAsync(config, discoverTimeout.Token);
            }
            catch (SocketException ex)
            {
                return OpenSessionResult.FromFailure($"Discovery strategy failed: {ex.Message}");
            }
        }

        if (discoveredHostAddress is null)
        {
            return OpenSessionResult.FromFailure("No host discovered.");
        }

        progress?.Report($"Discovered host at {discoveredHostAddress}:{config.Host.DiscoveryPort}");

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        ConnectResponse? connectResponse;
        try
        {
            connectResponse = await ConnectAsync(config, clientName, discoveredHostAddress, connectTimeout.Token);
        }
        catch (SocketException ex)
        {
            return OpenSessionResult.FromFailure($"UDP connect failed: {ex.Message}");
        }

        if (connectResponse is null)
        {
            return OpenSessionResult.FromFailure("No connect response from host.");
        }

        progress?.Report($"Host response: {connectResponse.Message}");
        progress?.Report($"Reason: {connectResponse.Reason}");
        progress?.Report($"Accepted: {connectResponse.Accepted}");

        if (!connectResponse.Accepted)
        {
            return OpenSessionResult.FromRejected("Host rejected the connection request.", discoveredHostAddress, connectResponse);
        }

        if (connectResponse.WebSocketPort is null ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketPath) ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketToken))
        {
            return OpenSessionResult.FromFailure("Host did not provide a WebSocket handoff.");
        }

        var wsUri = new Uri(
            $"ws://{discoveredHostAddress}:{connectResponse.WebSocketPort}{connectResponse.WebSocketPath}?token={Uri.EscapeDataString(connectResponse.WebSocketToken)}");
        progress?.Report($"Opening WebSocket: {wsUri}");

        var connectedSocket = await ConnectWebSocketWithRetryAsync(wsUri, cancellationToken);
        if (connectedSocket is null)
        {
            return OpenSessionResult.FromFailure("WebSocket endpoint did not become reachable in time.");
        }

        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));

            var hostHello = await ReceiveTextAsync(connectedSocket, helloTimeout.Token);
            progress?.Report($"WS <- {hostHello}");

            _webSocket = connectedSocket;
            _connectResponse = connectResponse;

            return OpenSessionResult.FromSuccess("Connected to host and WebSocket session is ready.", discoveredHostAddress, connectResponse, hostHello);
        }
        catch (Exception ex)
        {
            connectedSocket.Dispose();
            return OpenSessionResult.FromFailure($"WebSocket handshake failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> SendClientLogAsync(string logLine, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var webSocket = _webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        if (string.IsNullOrWhiteSpace(logLine))
        {
            return OperationResult.FromFailure("Enter log text before sending.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_LOG",
            sentAtUtc = DateTimeOffset.UtcNow,
            data = logLine.Trim()
        }, PairingJson.Compact);

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await SendTextAsync(webSocket, payload, sendTimeout.Token);
                progress?.Report($"WS -> {payload}");

                using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ackTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                var hostAck = await ReceiveTextAsync(webSocket, ackTimeout.Token);
                progress?.Report($"WS <- {hostAck}");

                if (string.Equals(hostAck, "<close>", StringComparison.Ordinal))
                {
                    await CloseSessionAsync(CancellationToken.None);
                    return OperationResult.FromFailure("Host closed the WebSocket session.");
                }
            }
            finally
            {
                _sendLock.Release();
            }

            return OperationResult.FromSuccess("Log sent.");
        }
        catch (Exception ex)
        {
            await CloseSessionAsync(CancellationToken.None);
            return OperationResult.FromFailure($"Failed to send log: {ex.Message}");
        }
    }

    public async Task<OperationResult> CompleteSessionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var webSocket = _webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_DONE",
            sentAtUtc = DateTimeOffset.UtcNow,
            data = "client log stream complete"
        }, PairingJson.Compact);

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await SendTextAsync(webSocket, payload, sendTimeout.Token);
                progress?.Report($"WS -> {payload}");

                using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ackTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                var doneAck = await ReceiveTextAsync(webSocket, ackTimeout.Token);
                progress?.Report($"WS <- {doneAck}");
            }
            finally
            {
                _sendLock.Release();
            }

            await CloseSessionAsync(CancellationToken.None);
            return OperationResult.FromSuccess("Session complete.");
        }
        catch (Exception ex)
        {
            await CloseSessionAsync(CancellationToken.None);
            return OperationResult.FromFailure($"Failed to complete session: {ex.Message}");
        }
    }

    public async Task<OperationResult> StartMetricsStreamingAsync(
        IDataSink dataSink,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSink);

        var webSocket = _webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        await StopMetricsStreamingAsync(progress: null, CancellationToken.None);

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

        // Re-announce channels after clearing channel state so reconnects have a fresh map.
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

    public async Task<OperationResult> StopMetricsStreamingAsync(IProgress<string>? progress, CancellationToken cancellationToken)
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

        if (pumpCts is not null)
        {
            pumpCts.Cancel();
        }

        if (eventsPumpCts is not null)
        {
            eventsPumpCts.Cancel();
        }

        if (pumpTask is not null)
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

        if (eventsPumpTask is not null)
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

    public async Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken)
    {
        await StopMetricsStreamingAsync(progress: null, CancellationToken.None);

        var webSocket = _webSocket;

        _webSocket = null;
        _connectResponse = null;

        if (webSocket is null)
        {
            return OperationResult.FromSuccess("Session already closed.");
        }

        try
        {
            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
            {
                using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                closeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client session closed.", closeTimeout.Token);
            }
        }
        catch
        {
            // Ignore close errors; socket is still disposed.
        }
        finally
        {
            webSocket.Dispose();
        }

        return OperationResult.FromSuccess("Session disconnected.");
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

                var metricsResult = await SendMetricsBatchAsync(batch, progress, cancellationToken);
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

        var sendResult = await SendWithoutAcknowledgementAsync(payload, cancellationToken);
        if (!sendResult.Success)
        {
            return sendResult;
        }

        progress?.Report($"WS -> announced {newChannels.Length} metric channels");
        return OperationResult.FromSuccess("Metric channel definitions sent.");
    }

    private async Task<OperationResult> SendMetricsBatchAsync(
        IReadOnlyList<Metric> metrics,
        IProgress<string>? progress,
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

        var sendResult = await SendWithoutAcknowledgementAsync(payload, cancellationToken);
        if (!sendResult.Success)
        {
            return sendResult;
        }

        return OperationResult.FromSuccess("Metric samples sent.");
    }

    private async Task<OperationResult> SendEventsBatchAsync(
        IReadOnlyList<AppEvent> events,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return OperationResult.FromSuccess("No events to stream.");
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

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await SendTextAsync(_webSocket!, payload, sendTimeout.Token);
                progress?.Report($"WS -> streamed {events.Count} events");

                using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ackTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                var hostAck = await ReceiveTextAsync(_webSocket!, ackTimeout.Token);
                progress?.Report($"WS <- {hostAck}");

                if (string.Equals(hostAck, "<close>", StringComparison.Ordinal))
                {
                    await CloseSessionAsync(CancellationToken.None);
                    return OperationResult.FromFailure("Host closed the WebSocket session.");
                }
            }
            finally
            {
                _sendLock.Release();
            }

            return OperationResult.FromSuccess("Event batch sent.");
        }
        catch (Exception ex)
        {
            await CloseSessionAsync(CancellationToken.None);
            return OperationResult.FromFailure($"Failed to send events: {ex.Message}");
        }
    }

    private async Task<OperationResult> SendWithoutAcknowledgementAsync(string payload, CancellationToken cancellationToken)
    {
        var webSocket = _webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await SendTextAsync(webSocket, payload, sendTimeout.Token);
            }
            finally
            {
                _sendLock.Release();
            }

            return OperationResult.FromSuccess("Payload sent.");
        }
        catch (Exception ex)
        {
            return OperationResult.FromFailure($"Failed to send WebSocket payload: {ex.Message}");
        }
    }

    private static string ToColorHex(System.Drawing.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool VerifyPairingConfigSignature(PairingConfig config)
    {
        try
        {
            var publicKey = Convert.FromBase64String(config.Host.HostPubKey);
            var signature = Convert.FromBase64String(config.Signature);

            using var hostKey = ECDsa.Create();
            hostKey.ImportSubjectPublicKeyInfo(publicKey, out _);

            var signables = new[]
            {
                PairingCanonicalJson.SerializePairingConfigForSignature(config),
                PairingCanonicalJson.SerializePairingConfigForSignatureWithoutHostIdentity(config),
                PairingCanonicalJson.SerializeLegacyPairingConfigForSignature(config),
                PairingCanonicalJson.SerializeLegacyPairingConfigForSignatureWithoutHostIdentity(config)
            };

            foreach (var signable in signables)
            {
                if (hostKey.VerifyData(Encoding.UTF8.GetBytes(signable), signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateAppId(PairingConfig config, string? expectedAppId, out string error)
    {
        var configuredAppId = config.AppId?.Trim() ?? string.Empty;
        var normalizedExpected = expectedAppId?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            error = string.Empty;
            return true;
        }

        if (!string.Equals(configuredAppId, normalizedExpected, StringComparison.Ordinal))
        {
            error = $"Config appId '{configuredAppId}' does not match expected app id '{normalizedExpected}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryParseDocument(string configJson, out ParsedPairingDocument? document, out string error)
    {
        document = null;

        try
        {
            using var json = JsonDocument.Parse(configJson);
            var root = json.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Config JSON root must be an object.";
                return false;
            }

            var schema = root.TryGetProperty("schema", out var schemaElement)
                ? schemaElement.GetString()
                : null;

            if (string.Equals(schema, PairingBootstrapDocument.SchemaName, StringComparison.Ordinal))
            {
                var bootstrap = JsonSerializer.Deserialize<PairingBootstrapDocument>(configJson, PairingJson.Compact);
                if (bootstrap?.PairingConfig is null)
                {
                    error = "Bootstrap document did not contain a pairing config.";
                    return false;
                }

                document = new ParsedPairingDocument
                {
                    Config = bootstrap.PairingConfig,
                    DiscoveryHint = bootstrap.Discovery
                };

                error = string.Empty;
                return true;
            }

            var config = JsonSerializer.Deserialize<PairingConfig>(configJson, PairingJson.Compact);
            if (config is null)
            {
                error = "Config JSON is empty.";
                return false;
            }

            document = new ParsedPairingDocument
            {
                Config = config,
                DiscoveryHint = null
            };

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse config JSON: {ex.Message}";
            return false;
        }
    }

    private static async Task<ClientWebSocket?> ConnectWebSocketWithRetryAsync(Uri wsUri, CancellationToken cancellationToken)
    {
        const int maxAttempts = 12;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var webSocket = new ClientWebSocket();

            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                await webSocket.ConnectAsync(wsUri, connectTimeout.Token);

                if (webSocket.State == WebSocketState.Open)
                {
                    return webSocket;
                }
            }
            catch when (attempt < maxAttempts)
            {
                // Retry.
            }
            catch
            {
                webSocket.Dispose();
                throw;
            }

            webSocket.Dispose();

            if (attempt < maxAttempts)
            {
                await Task.Delay(250, cancellationToken);
            }
        }

        return null;
    }

    private static async Task<ConnectResponse?> ConnectAsync(
        PairingConfig config,
        string clientName,
        IPAddress hostAddress,
        CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient(0);

        var request = new ConnectRequest
        {
            Type = "CONNECT_REQ",
            Ver = 1,
            ConfigId = config.ConfigId,
            OneTimeToken = config.OneTimeToken,
            AppId = config.AppId,
            ClientName = clientName
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, PairingJson.Compact);
        await udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(hostAddress, config.Host.DiscoveryPort));

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult receiveResult;
            try
            {
                receiveResult = await udpClient.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (!Equals(receiveResult.RemoteEndPoint.Address, hostAddress))
            {
                continue;
            }

            try
            {
                var response = JsonSerializer.Deserialize<ConnectResponse>(receiveResult.Buffer, PairingJson.Compact);
                if (response is not null && string.Equals(response.Type, "CONNECT_RESP", StringComparison.Ordinal))
                {
                    return response;
                }
            }
            catch
            {
                // Ignore malformed packets.
            }
        }

        return null;
    }

    private static bool TryResolveManualHostAddress(string? manualHostAddress, out IPAddress? hostAddress)
    {
        hostAddress = null;
        if (string.IsNullOrWhiteSpace(manualHostAddress))
        {
            return false;
        }

        return IPAddress.TryParse(manualHostAddress.Trim(), out hostAddress);
    }

    private static async Task SendTextAsync(ClientWebSocket webSocket, string payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);
        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return "<close>";
            }

            if (result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _metricsPumpCts?.Cancel();

        if (_metricsDataSink is not null && _metricsUpdatedHandler is not null)
        {
            _metricsDataSink.OnMetricsUpdated -= _metricsUpdatedHandler;
        }

        _metricsPumpCts?.Dispose();
        _webSocket?.Dispose();
        _sendLock.Dispose();
        _metricsSignal.Dispose();
        _webSocket = null;
        _connectResponse = null;
        _metricsDataSink = null;
        _metricsUpdatedHandler = null;
        _metricsPumpTask = null;
        _metricsPumpCts = null;
    }
}

public sealed record OpenSessionResult(
    bool Success,
    bool Accepted,
    string Message,
    IPAddress? HostAddress,
    ConnectResponse? ConnectResponse,
    string? HostHello)
{
    public static OpenSessionResult FromFailure(string message) => new(false, false, message, null, null, null);

    public static OpenSessionResult FromRejected(string message, IPAddress hostAddress, ConnectResponse connectResponse) =>
        new(false, false, message, hostAddress, connectResponse, null);

    public static OpenSessionResult FromSuccess(
        string message,
        IPAddress hostAddress,
        ConnectResponse connectResponse,
        string hostHello) =>
        new(true, true, message, hostAddress, connectResponse, hostHello);
}

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult FromSuccess(string message) => new(true, message);

    public static OperationResult FromFailure(string message) => new(false, message);
}
