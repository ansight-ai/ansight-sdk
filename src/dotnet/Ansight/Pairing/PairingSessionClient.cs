using System.Net;
using System.Text.Json;

namespace Ansight.Pairing;

public sealed class PairingSessionClient : IDisposable
{
    private readonly PairingConfigDocumentService _configDocumentService = new();
    private readonly DeviceAppProfileResolver _deviceAppProfileResolver;
    private readonly PairingSessionConnector _connector;
    private readonly PairingSessionTransport _transport;
    private readonly PairingTelemetryStreamer _telemetryStreamer;
    private readonly PairingSessionJpegStreamer _jpegStreamer;
    private bool _disposed;

    public PairingSessionClient()
        : this(hostDiscoveryStrategy: null, deviceAppProfileProvider: null)
    {
    }

    public PairingSessionClient(IPairingHostDiscoveryStrategy? hostDiscoveryStrategy)
        : this(hostDiscoveryStrategy, deviceAppProfileProvider: null)
    {
    }

    public PairingSessionClient(
        IPairingHostDiscoveryStrategy? hostDiscoveryStrategy,
        IDeviceAppProfileProvider? deviceAppProfileProvider)
    {
        var profileProvider = deviceAppProfileProvider ?? AutomaticDeviceAppProfileProvider.Instance;

        _deviceAppProfileResolver = new DeviceAppProfileResolver(profileProvider);
        _connector = new PairingSessionConnector(hostDiscoveryStrategy);
        _transport = new PairingSessionTransport();
        _telemetryStreamer = new PairingTelemetryStreamer(_transport);
        _jpegStreamer = new PairingSessionJpegStreamer(_transport);
    }

    public static PairingSessionClientBuilder CreateBuilder() => new();

    public bool TryParseAndValidateDocument(string configJson, string? expectedAppId, out ParsedPairingDocument? document, out string error)
        => _configDocumentService.TryParseAndValidateDocument(configJson, expectedAppId, out document, out error);

    public bool TryParseAndValidateConfig(string configJson, string? expectedAppId, out PairingConfig? config, out string error)
        => _configDocumentService.TryParseAndValidateConfig(configJson, expectedAppId, out config, out error);

    public bool TryValidateConfig(PairingConfig config, string? expectedAppId, out string error)
        => _configDocumentService.TryValidateConfig(config, expectedAppId, out error);

    public bool TryParseDocument(string configJson, out ParsedPairingDocument? document, out string error)
        => _configDocumentService.TryParseDocument(configJson, out document, out error);

    public Task<OpenSessionResult> OpenSessionAsync(
        PairingConfig config,
        string clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return OpenSessionAsync(config, clientName, options: null, progress, cancellationToken);
    }

    public async Task<OpenSessionResult> OpenSessionAsync(
        PairingConfig config,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        await CloseSessionAsync(CancellationToken.None);

        var deviceAppProfile = _deviceAppProfileResolver.Resolve(options?.DeviceAppProfile);
        var expectedAppId = _deviceAppProfileResolver.ResolveExpectedAppId(deviceAppProfile);
        if (!TryValidateConfig(config, expectedAppId, out var validationError))
        {
            return OpenSessionResult.FromFailure(validationError);
        }

        progress?.Report($"Config validated. ConfigId: {config.ConfigId}");

        var connectionAttempt = await _connector.ConnectAsync(config, clientName, options, progress, cancellationToken);
        if (!connectionAttempt.Success)
        {
            return connectionAttempt.Accepted
                ? OpenSessionResult.FromFailure(connectionAttempt.Message)
                : connectionAttempt.ConnectResponse is null || connectionAttempt.HostAddress is null
                    ? OpenSessionResult.FromFailure(connectionAttempt.Message)
                    : OpenSessionResult.FromRejected(connectionAttempt.HostAddress, connectionAttempt.ConnectResponse);
        }

        try
        {
            _transport.Attach(connectionAttempt.WebSocket!);

            if (deviceAppProfile is not null)
            {
                var profileResult = await SendDeviceAppProfileAsync(deviceAppProfile, progress, cancellationToken);
                if (!profileResult.Success)
                {
                    await CloseSessionAsync(CancellationToken.None);
                    return OpenSessionResult.FromFailure(profileResult.Message);
                }
            }

            await _jpegStreamer.StartAsync(progress);

            return OpenSessionResult.FromSuccess(
                connectionAttempt.Message,
                connectionAttempt.HostAddress!,
                connectionAttempt.ConnectResponse!,
                connectionAttempt.HostHello!);
        }
        catch
        {
            connectionAttempt.WebSocket?.Dispose();
            throw;
        }
    }

    public Task<OperationResult> SendClientLogAsync(string logLine, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return Task.FromResult(OperationResult.FromFailure("Enter log text before sending."));
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_LOG",
            sentAtUtc = DateTimeOffset.UtcNow,
            data = logLine.Trim()
        }, PairingJson.Compact);

        return _transport.SendRequestAsync(
            payload,
            $"WS -> {payload}",
            "Log sent.",
            "Failed to send log",
            progress,
            TimeSpan.FromSeconds(15),
            cancellationToken);
    }

    public Task<OperationResult> SendDeviceAppProfileAsync(
        DeviceAppProfile profile,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _deviceAppProfileResolver.NormalizeForSend(profile);
        var payload = JsonSerializer.Serialize(profile, PairingJson.Compact);

        return _transport.SendRequestAsync(
            payload,
            "WS -> DeviceAppProfile",
            "Device profile sent.",
            "Failed to send device profile",
            progress,
            TimeSpan.FromSeconds(15),
            cancellationToken);
    }

    public async Task<OperationResult> CompleteSessionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_DONE",
            sentAtUtc = DateTimeOffset.UtcNow,
            data = "client log stream complete"
        }, PairingJson.Compact);

        var result = await _transport.SendRequestAsync(
            payload,
            $"WS -> {payload}",
            "Session complete.",
            "Failed to complete session",
            progress,
            TimeSpan.FromSeconds(10),
            cancellationToken);

        await CloseSessionAsync(CancellationToken.None);
        return result;
    }

    public Task<ToolProtocolProcessResult> ProcessToolProtocolMessageAsync(string messageJson, CancellationToken cancellationToken)
        => PairingToolProtocolProcessor.ProcessAsync(messageJson, cancellationToken);

    public Task<OperationResult> StartMetricsStreamingAsync(
        IDataSink dataSink,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return _telemetryStreamer.StartAsync(dataSink, progress, cancellationToken);
    }

    public Task<OperationResult> StopMetricsStreamingAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        return _telemetryStreamer.StopAsync(progress, cancellationToken);
    }

    public async Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken)
    {
        await _telemetryStreamer.StopAsync(progress: null, CancellationToken.None);
        await _jpegStreamer.StopAsync(CancellationToken.None);
        return await _transport.CloseAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _telemetryStreamer.Dispose();
        _jpegStreamer.Dispose();
        _transport.Dispose();
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

    public string? RejectionCode => Accepted ? null : ConnectResponse?.Reason;

    public string? RejectionReason => Accepted
        ? null
        : FirstNonEmpty(ConnectResponse?.ReasonMessage, ConnectResponse?.Message, Message);

    public static OpenSessionResult FromRejected(IPAddress hostAddress, ConnectResponse connectResponse) =>
        new(false, false, FirstNonEmpty(connectResponse.ReasonMessage, connectResponse.Message, "Host rejected the connection request."), hostAddress, connectResponse, null);

    public static OpenSessionResult FromSuccess(
        string message,
        IPAddress hostAddress,
        ConnectResponse connectResponse,
        string hostHello) =>
        new(true, true, message, hostAddress, connectResponse, hostHello);

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult FromSuccess(string message) => new(true, message);

    public static OperationResult FromFailure(string message) => new(false, message);
}

public sealed record ToolProtocolProcessResult(bool Success, string Message, string? ResponseJson)
{
    public static ToolProtocolProcessResult FromSuccess(string responseJson)
        => new(true, "Tool protocol message processed.", responseJson);

    public static ToolProtocolProcessResult FromFailure(string message)
        => new(false, message, null);
}
