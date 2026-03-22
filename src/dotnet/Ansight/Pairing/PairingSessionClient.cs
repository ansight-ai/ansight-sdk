using System.Net;
using System.Text.Json;

namespace Ansight.Pairing;

public sealed class PairingSessionClient : IDisposable
{
    private readonly PairingConfigDocumentService configDocumentService = new();
    private readonly DeviceAppProfileResolver deviceAppProfileResolver;
    private readonly PairingSessionConnector connector;
    private readonly PairingSessionTransport transport;
    private readonly PairingSessionAppStateStreamer appStateStreamer;
    private readonly TelemetryStreamer telemetryStreamer;
    private readonly PairingSessionJpegStreamer jpegStreamer;
    private bool disposed;

    public PairingSessionClient()
        : this(deviceAppProfileProvider: null)
    {
    }

    public PairingSessionClient(IDeviceAppProfileProvider? deviceAppProfileProvider)
    {
        var profileProvider = deviceAppProfileProvider ?? AutomaticDeviceAppProfileProvider.Instance;

        deviceAppProfileResolver = new DeviceAppProfileResolver(profileProvider);
        connector = new PairingSessionConnector();
        transport = new PairingSessionTransport();
        appStateStreamer = new PairingSessionAppStateStreamer(transport);
        telemetryStreamer = new TelemetryStreamer(transport);
        jpegStreamer = new PairingSessionJpegStreamer(transport);
    }

    public static PairingSessionClientBuilder CreateBuilder() => new();

    public bool TryParseAndValidateDocument(string configJson, string? expectedAppId, out ParsedPairingDocument? document, out string error)
        => configDocumentService.TryParseAndValidateDocument(configJson, expectedAppId, out document, out error);

    public bool TryParseAndValidateConfig(string configJson, string? expectedAppId, out PairingConfig? config, out string error)
        => configDocumentService.TryParseAndValidateConfig(configJson, expectedAppId, out config, out error);

    public bool TryValidateConfig(PairingConfig config, string? expectedAppId, out string error)
        => configDocumentService.TryValidateConfig(config, expectedAppId, out error);

    public bool TryValidateDocument(ParsedPairingDocument document, string? expectedAppId, out string error)
        => configDocumentService.TryValidateDocument(document, expectedAppId, out error);

    public bool TryParseDocument(string configJson, out ParsedPairingDocument? document, out string error)
        => configDocumentService.TryParseDocument(configJson, out document, out error);

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

        return await OpenSessionAsync(
            new ParsedPairingDocument
            {
                Config = config
            },
            clientName,
            options,
            progress,
            cancellationToken);
    }

    public Task<OpenSessionResult> OpenSessionAsync(
        ParsedPairingDocument document,
        string clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return OpenSessionAsync(document, clientName, options: null, progress, cancellationToken);
    }

    public async Task<OpenSessionResult> OpenSessionAsync(
        ParsedPairingDocument document,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await CloseSessionAsync(CancellationToken.None);

        var deviceAppProfile = deviceAppProfileResolver.Resolve(options?.DeviceAppProfile);
        var expectedAppId = deviceAppProfileResolver.ResolveExpectedAppId(deviceAppProfile);
        if (!TryValidateDocument(document, expectedAppId, out var validationError))
        {
            return OpenSessionResult.FromFailure(validationError);
        }

        var config = document.Config;
        progress?.Report($"Config validated. ConfigId: {config.ConfigId}");

        var connectionAttempt = await connector.ConnectAsync(config, clientName, options, progress, cancellationToken);
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
            transport.Attach(connectionAttempt.WebSocket!);
            if (Runtime.IsInitialized)
            {
                Runtime.MutableInstance.BinaryTransferHub.AttachTransport(transport);
            }

            if (deviceAppProfile is not null)
            {
                var profileResult = await SendDeviceAppProfileAsync(deviceAppProfile, progress, cancellationToken);
                if (!profileResult.Success)
                {
                    await CloseSessionAsync(CancellationToken.None);
                    return OpenSessionResult.FromFailure(profileResult.Message);
                }
            }

            var appStateResult = await appStateStreamer.StartAsync(progress, cancellationToken);
            if (!appStateResult.Success)
            {
                await CloseSessionAsync(CancellationToken.None);
                return OpenSessionResult.FromFailure(appStateResult.Message);
            }

            await jpegStreamer.StartAsync(progress);

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

        return transport.SendRequestAsync(
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

        deviceAppProfileResolver.NormalizeForSend(profile);
        var payload = JsonSerializer.Serialize(profile, PairingJson.Compact);

        return transport.SendRequestAsync(
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

        var result = await transport.SendRequestAsync(
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
        return telemetryStreamer.StartAsync(dataSink, progress, cancellationToken);
    }

    public Task<OperationResult> StopMetricsStreamingAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        return telemetryStreamer.StopAsync(progress, cancellationToken);
    }

    public async Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken)
    {
        await appStateStreamer.StopAsync(CancellationToken.None);
        await telemetryStreamer.StopAsync(progress: null, CancellationToken.None);
        await jpegStreamer.StopAsync(CancellationToken.None);
        var result = await transport.CloseAsync(cancellationToken);
        if (Runtime.IsInitialized)
        {
            Runtime.MutableInstance.BinaryTransferHub.DetachTransport(transport);
        }

        return result;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        appStateStreamer.Dispose();
        telemetryStreamer.Dispose();
        jpegStreamer.Dispose();
        if (Runtime.IsInitialized)
        {
            Runtime.MutableInstance.BinaryTransferHub.DetachTransport(transport);
        }

        transport.Dispose();
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
