using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ansight.Pairing;

/// <summary>
/// High-level client for validating pairing payloads, opening live host sessions, and sending session metadata over the pairing transport.
/// </summary>
public sealed class PairingSessionClient : IDisposable, IHostConnectionSessionClient
{
    private static readonly HashSet<string> CachedProfileResetCodes = new(StringComparer.Ordinal)
    {
        PairingFailureCodes.PairingRequired,
        PairingFailureCodes.PairingTokenInvalid,
        PairingFailureCodes.PairingTokenExpired,
        PairingFailureCodes.PairingProofInvalid,
        PairingFailureCodes.UdpBootstrapFailed,
        PairingFailureCodes.UdpBootstrapTimeout
    };

    private readonly PairingConfigDocumentService configDocumentService = new();
    private readonly DeviceAppProfileResolver deviceAppProfileResolver;
    private readonly PairingSessionConnector connector;
    private readonly PairingSessionTransport transport;
    private readonly PairingSessionAppStateStreamer appStateStreamer;
    private readonly TelemetryStreamer telemetryStreamer;
    private readonly PairingSessionJpegStreamer jpegStreamer;
    private readonly StoredPairingDocumentCache storedPairingDocumentCache;
    private bool disposed;

    /// <summary>
    /// Creates a pairing session client that uses the default automatic device app profile provider.
    /// </summary>
    public PairingSessionClient()
        : this(deviceAppProfileProvider: null, storedPairingDocumentCache: null)
    {
    }

    /// <summary>
    /// Creates a pairing session client with a custom baseline device app profile provider.
    /// </summary>
    /// <param name="deviceAppProfileProvider">Provider used to create the automatic baseline device app profile, or <see langword="null"/> to use the default collector.</param>
    public PairingSessionClient(IDeviceAppProfileProvider? deviceAppProfileProvider)
        : this(deviceAppProfileProvider, storedPairingDocumentCache: null)
    {
    }

    internal PairingSessionClient(
        IDeviceAppProfileProvider? deviceAppProfileProvider,
        StoredPairingDocumentCache? storedPairingDocumentCache)
    {
        var profileProvider = deviceAppProfileProvider ?? AutomaticDeviceAppProfileProvider.Instance;

        deviceAppProfileResolver = new DeviceAppProfileResolver(profileProvider);
        connector = new PairingSessionConnector();
        transport = new PairingSessionTransport();
        appStateStreamer = new PairingSessionAppStateStreamer(transport);
        telemetryStreamer = new TelemetryStreamer(transport);
        jpegStreamer = new PairingSessionJpegStreamer(transport);
        this.storedPairingDocumentCache = storedPairingDocumentCache
                                         ?? new StoredPairingDocumentCache(StoredPairingDocumentCache.ResolveCacheKey(profileProvider));
        transport.Closed += HandleTransportClosed;
    }

    /// <summary>
    /// Creates a fluent builder for configuring a <see cref="PairingSessionClient"/>.
    /// </summary>
    /// <returns>A new pairing session client builder.</returns>
    public static PairingSessionClientBuilder CreateBuilder() => new();

    internal event EventHandler? SessionClosed;

    internal bool IsSessionOpen => transport.IsOpen;

    internal bool HasCachedPairingProfile => storedPairingDocumentCache.HasCachedDocument;

    event EventHandler? IHostConnectionSessionClient.SessionClosed
    {
        add => SessionClosed += value;
        remove => SessionClosed -= value;
    }

    bool IHostConnectionSessionClient.IsSessionOpen => IsSessionOpen;

    bool IHostConnectionSessionClient.HasCachedPairingProfile => HasCachedPairingProfile;

    bool IHostConnectionSessionClient.TryParseAndValidateDocument(
        string configJson,
        out ParsedPairingDocument? document,
        out string error)
        => TryParseAndValidateDocument(
            configJson,
            deviceAppProfileResolver.ResolveExpectedAppId(deviceAppProfileResolver.Resolve(callerProfile: null)),
            out document,
            out error);

    /// <summary>
    /// Parses a pairing document and validates its signature, expiry, and expected app id.
    /// </summary>
    /// <param name="configJson">JSON payload containing either a pairing config document or a pairing config.</param>
    /// <param name="expectedAppId">Optional app id that the payload must target.</param>
    /// <param name="document">Parsed pairing document when validation succeeds.</param>
    /// <param name="error">Validation or parsing error message when the operation fails.</param>
    /// <returns><see langword="true"/> when parsing and validation both succeed; otherwise, <see langword="false"/>.</returns>
    public bool TryParseAndValidateDocument(string configJson, string? expectedAppId, out ParsedPairingDocument? document, out string error)
        => configDocumentService.TryParseAndValidateDocument(configJson, expectedAppId, out document, out error);

    /// <summary>
    /// Parses and validates a pairing document, returning the effective pairing config when successful.
    /// </summary>
    /// <param name="configJson">JSON payload containing either a pairing config document or a pairing config.</param>
    /// <param name="expectedAppId">Optional app id that the payload must target.</param>
    /// <param name="config">Effective pairing config when parsing and validation succeed.</param>
    /// <param name="error">Validation or parsing error message when the operation fails.</param>
    /// <returns><see langword="true"/> when parsing and validation both succeed; otherwise, <see langword="false"/>.</returns>
    public bool TryParseAndValidateConfig(string configJson, string? expectedAppId, out PairingConfig? config, out string error)
        => configDocumentService.TryParseAndValidateConfig(configJson, expectedAppId, out config, out error);

    /// <summary>
    /// Validates a pairing config against its signature, expiry, and optional expected app id.
    /// </summary>
    /// <param name="config">Config to validate.</param>
    /// <param name="expectedAppId">Optional app id that the config must target.</param>
    /// <param name="error">Validation error message when the operation fails.</param>
    /// <returns><see langword="true"/> when validation succeeds; otherwise, <see langword="false"/>.</returns>
    public bool TryValidateConfig(PairingConfig config, string? expectedAppId, out string error)
        => configDocumentService.TryValidateConfig(config, expectedAppId, out error);

    /// <summary>
    /// Validates a parsed pairing document against its signature, expiry, and optional expected app id.
    /// </summary>
    /// <param name="document">Parsed document to validate.</param>
    /// <param name="expectedAppId">Optional app id that the document must target.</param>
    /// <param name="error">Validation error message when the operation fails.</param>
    /// <returns><see langword="true"/> when validation succeeds; otherwise, <see langword="false"/>.</returns>
    public bool TryValidateDocument(ParsedPairingDocument document, string? expectedAppId, out string error)
        => configDocumentService.TryValidateDocument(document, expectedAppId, out error);

    /// <summary>
    /// Parses a pairing config document without validating it.
    /// </summary>
    /// <param name="configJson">JSON payload containing a pairing config document.</param>
    /// <param name="document">Parsed pairing document when parsing succeeds.</param>
    /// <param name="error">Parsing error message when the operation fails.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise, <see langword="false"/>.</returns>
    public bool TryParseDocument(string configJson, out ParsedPairingDocument? document, out string error)
        => configDocumentService.TryParseDocument(configJson, out document, out error);

    /// <summary>
    /// Opens a pairing session from a validated pairing config using default connection options.
    /// </summary>
    /// <param name="config">Signed pairing config to use for the session.</param>
    /// <param name="clientName">Name reported to the host for this client connection.</param>
    /// <param name="progress">Optional progress sink for structured connection updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of attempting to open the session.</returns>
    public Task<OpenSessionResult> OpenSessionAsync(
        PairingConfig config,
        string clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        return OpenSessionAsync(config, clientName, options: null, progress, cancellationToken);
    }

    /// <summary>
    /// Opens a pairing session from a validated pairing config.
    /// </summary>
    /// <param name="config">Signed pairing config to use for the session.</param>
    /// <param name="clientName">Name reported to the host for this client connection.</param>
    /// <param name="options">Optional discovery and profile overrides for the connection attempt.</param>
    /// <param name="progress">Optional progress sink for structured connection updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of attempting to open the session.</returns>
    public async Task<OpenSessionResult> OpenSessionAsync(
        PairingConfig config,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
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

    /// <summary>
    /// Opens a pairing session from a parsed pairing document using default connection options.
    /// </summary>
    /// <param name="document">Parsed pairing document to validate and use for the session.</param>
    /// <param name="clientName">Name reported to the host for this client connection.</param>
    /// <param name="progress">Optional progress sink for structured connection updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of attempting to open the session.</returns>
    public Task<OpenSessionResult> OpenSessionAsync(
        ParsedPairingDocument document,
        string clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        return OpenSessionAsync(document, clientName, options: null, progress, cancellationToken);
    }

    /// <summary>
    /// Opens a pairing session from a parsed pairing document.
    /// </summary>
    /// <param name="document">Parsed pairing document to validate and use for the session.</param>
    /// <param name="clientName">Name reported to the host for this client connection.</param>
    /// <param name="options">Optional discovery and profile overrides for the connection attempt.</param>
    /// <param name="progress">Optional progress sink for structured connection updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of attempting to open the session.</returns>
    public async Task<OpenSessionResult> OpenSessionAsync(
        ParsedPairingDocument document,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
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
        var discoveryPort = PairingDiscoveryPortResolver.Resolve(document, options?.DiscoveryPort);
        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.Validation,
            $"Config validated. ConfigId: {config.ConfigId}",
            source: HostConnectionSource.Payload);

        var connectionAttempt = await connector.ConnectAsync(document, clientName, options, progress, cancellationToken);
        if (!connectionAttempt.Success)
        {
            return connectionAttempt.Accepted
                ? OpenSessionResult.FromFailure(connectionAttempt.Message, connectionAttempt.FailureCode)
                : connectionAttempt.ConnectResponse is null || connectionAttempt.HostAddress is null
                    ? OpenSessionResult.FromFailure(connectionAttempt.Message, connectionAttempt.FailureCode)
                    : OpenSessionResult.FromRejected(connectionAttempt.HostAddress, connectionAttempt.ConnectResponse);
        }

        try
        {
            transport.Attach(connectionAttempt.WebSocket!);
            if (Runtime.IsInitialized)
            {
                Runtime.MutableInstance.BinaryTransferHub.AttachTransport(transport);
            }

            var sessionOpenResult = await SendSessionOpenAsync(config, clientName, progress, cancellationToken);
            if (!sessionOpenResult.Success)
            {
                await CloseSessionAsync(CancellationToken.None);
                return OpenSessionResult.FromFailure(sessionOpenResult.Message);
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
            try
            {
                storedPairingDocumentCache.Save(CreateCachedDocument(document, connectionAttempt.HostAddress, discoveryPort));
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to cache the host session for auto-probe: {ex.Message}");
            }

            return OpenSessionResult.FromSuccess(
                connectionAttempt.Message,
                connectionAttempt.HostAddress!,
                connectionAttempt.ConnectResponse!);
        }
        catch
        {
            connectionAttempt.WebSocket?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends a single client log line to the connected host over the live pairing session.
    /// </summary>
    /// <param name="logLine">Log line to send.</param>
    /// <param name="progress">Optional progress sink for structured transport updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of sending the log line.</returns>
    public Task<OperationResult> SendClientLogAsync(string logLine, IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return Task.FromResult(OperationResult.FromFailure("Enter log text before sending."));
        }

        var payload = JsonSerializer.Serialize(new
        {
            data = logLine.Trim()
        }, PairingJson.Compact);

        return transport.SendControlRequestAsync(
            PairingControlActions.ClientLog,
            JsonSerializer.Deserialize<JsonObject>(payload, PairingJson.Compact),
            "WS -> client.log",
            "Log sent.",
            "Failed to send log",
            progress,
            TimeSpan.FromSeconds(15),
            cancellationToken,
            HostConnectionSource.Transport,
            HostConnectionProgressKind.Transport);
    }

    /// <summary>
    /// Sends a device app profile payload to the connected host.
    /// </summary>
    /// <param name="profile">Profile to normalize and send.</param>
    /// <param name="progress">Optional progress sink for structured transport updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of sending the profile.</returns>
    public Task<OperationResult> SendDeviceAppProfileAsync(
        DeviceAppProfile profile,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        deviceAppProfileResolver.NormalizeForSend(profile);
        var payload = JsonSerializer.SerializeToNode(profile, PairingJson.Compact) as JsonObject;

        return transport.SendControlRequestAsync(
            PairingControlActions.DeviceProfile,
            payload,
            "WS -> device.profile",
            "Device profile sent.",
            "Failed to send device profile",
            progress,
            TimeSpan.FromSeconds(15),
            cancellationToken,
            HostConnectionSource.HostConnection,
            HostConnectionProgressKind.Connection);
    }

    /// <summary>
    /// Sends the terminal client completion message and then closes the session transport.
    /// </summary>
    /// <param name="progress">Optional progress sink for structured transport updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of sending the completion message.</returns>
    public async Task<OperationResult> CompleteSessionAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["reason"] = "client log stream complete"
        };

        var result = await transport.SendControlRequestAsync(
            PairingControlActions.SessionComplete,
            payload,
            "WS -> session.complete",
            "Session complete.",
            "Failed to complete session",
            progress,
            TimeSpan.FromSeconds(10),
            cancellationToken,
            HostConnectionSource.Transport,
            HostConnectionProgressKind.Transport);

        await CloseSessionAsync(CancellationToken.None);
        return result;
    }

    /// <summary>
    /// Processes a raw tool-protocol message and returns the response payload to send back to the host when applicable.
    /// </summary>
    /// <param name="messageJson">Incoming tool-protocol message JSON.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The tool-protocol processing result.</returns>
    public Task<ToolProtocolProcessResult> ProcessToolProtocolMessageAsync(string messageJson, CancellationToken cancellationToken)
        => PairingToolProtocolProcessor.ProcessAsync(messageJson, cancellationToken);

    /// <summary>
    /// Starts streaming telemetry metrics from the supplied data sink to the connected host.
    /// </summary>
    /// <param name="dataSink">Telemetry data sink to observe.</param>
    /// <param name="progress">Optional progress sink for structured streaming updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of starting metrics streaming.</returns>
    public Task<OperationResult> StartMetricsStreamingAsync(
        IDataSink dataSink,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        return telemetryStreamer.StartAsync(dataSink, progress, cancellationToken);
    }

    /// <summary>
    /// Stops telemetry metrics streaming for the current session.
    /// </summary>
    /// <param name="progress">Optional progress sink for structured streaming updates.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of stopping metrics streaming.</returns>
    public Task<OperationResult> StopMetricsStreamingAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        return telemetryStreamer.StopAsync(progress, cancellationToken);
    }

    /// <summary>
    /// Stops session-owned streaming components and closes the live pairing transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The result of closing the session.</returns>
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

    internal async Task<OpenSessionResult> OpenCachedSessionAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var baselineProfile = deviceAppProfileResolver.Resolve(callerProfile: null);
        var expectedAppId = deviceAppProfileResolver.ResolveExpectedAppId(baselineProfile);
        if (!storedPairingDocumentCache.TryLoadValidated(expectedAppId, out var document, out var error) || document is null)
        {
            return OpenSessionResult.FromFailure(error);
        }

        if (string.IsNullOrWhiteSpace(PairingDiscoveryHintHostAddresses.ResolvePrimary(document.DiscoveryHint)))
        {
            storedPairingDocumentCache.Clear();
            return OpenSessionResult.FromFailure("Cached host session does not include a discovery host address and was cleared.");
        }

        var result = await OpenSessionAsync(
            document,
            ResolveClientName(clientName, baselineProfile),
            new PairingConnectionOptions
            {
                DiscoveryPort = PairingDiscoveryPortResolver.Resolve(document)
            },
            progress,
            cancellationToken);

        if (!result.Success && ShouldClearCachedPairingProfile(result))
        {
            storedPairingDocumentCache.Clear();
        }

        return result;
    }

    internal void ClearCachedPairingProfile()
    {
        storedPairingDocumentCache.Clear();
    }

    Task<OpenSessionResult> IHostConnectionSessionClient.OpenSessionAsync(
        ParsedPairingDocument document,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
        => OpenSessionAsync(document, clientName, options, progress, cancellationToken);

    Task<OpenSessionResult> IHostConnectionSessionClient.OpenCachedSessionAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
        => OpenCachedSessionAsync(clientName, progress, cancellationToken);

    void IHostConnectionSessionClient.ClearCachedPairingProfile()
        => ClearCachedPairingProfile();

    string IHostConnectionSessionClient.ResolveClientName(string? overrideClientName)
        => ResolveClientName(overrideClientName, deviceAppProfileResolver.Resolve(callerProfile: null));

    /// <summary>
    /// Disposes the client and releases any active transport and streaming resources.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        transport.Closed -= HandleTransportClosed;
        appStateStreamer.Dispose();
        telemetryStreamer.Dispose();
        jpegStreamer.Dispose();
        if (Runtime.IsInitialized)
        {
            Runtime.MutableInstance.BinaryTransferHub.DetachTransport(transport);
        }

        transport.Dispose();
    }

    private void HandleTransportClosed(object? sender, EventArgs e)
    {
        SessionClosed?.Invoke(this, EventArgs.Empty);
    }

    private Task<OperationResult> SendSessionOpenAsync(
        PairingConfig config,
        string clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["clientName"] = clientName,
            ["configId"] = config.ConfigId,
            ["appId"] = config.AppId,
            ["openedAtUtc"] = DateTimeOffset.UtcNow
        };

        return transport.SendControlRequestAsync(
            PairingControlActions.SessionOpen,
            payload,
            "WS -> session.open",
            "Session opened.",
            "Failed to open session",
            progress,
            TimeSpan.FromSeconds(15),
            cancellationToken,
            HostConnectionSource.Transport,
            HostConnectionProgressKind.Transport);
    }

    internal static ParsedPairingDocument CreateCachedDocument(
        ParsedPairingDocument document,
        IPAddress? connectedHostAddress,
        int? discoveryPort = null,
        DateTimeOffset? capturedAt = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var hostAddress = connectedHostAddress?.ToString()?.Trim();
        var existingDiscoveryHint = document.DiscoveryHint;
        var cachedHostAddresses = string.IsNullOrWhiteSpace(hostAddress)
            ? PairingDiscoveryHintHostAddresses.Normalize(existingDiscoveryHint)
            : new[] { hostAddress };
        PairingDiscoveryHint? cachedDiscoveryHint = null;
        if (existingDiscoveryHint is not null || cachedHostAddresses.Length > 0)
        {
            cachedDiscoveryHint = new PairingDiscoveryHint
            {
                Schema = string.IsNullOrWhiteSpace(existingDiscoveryHint?.Schema)
                    ? PairingDiscoveryHint.SchemaName
                    : existingDiscoveryHint.Schema,
                Source = existingDiscoveryHint?.Source ?? "live-session",
                HostAddresses = cachedHostAddresses.Length == 0 ? null : cachedHostAddresses,
                DiscoveryPort = discoveryPort ?? existingDiscoveryHint?.DiscoveryPort,
                HostName = existingDiscoveryHint?.HostName,
                WifiName = existingDiscoveryHint?.WifiName,
                CapturedAt = string.IsNullOrWhiteSpace(hostAddress)
                    ? existingDiscoveryHint?.CapturedAt
                    : capturedAt ?? DateTimeOffset.UtcNow
            };
        }

        return new ParsedPairingDocument
        {
            Config = document.Config,
            DiscoveryHint = cachedDiscoveryHint
        };
    }

    private static bool ShouldClearCachedPairingProfile(OpenSessionResult result)
    {
        var resetCode = string.IsNullOrWhiteSpace(result.RejectionCode)
            ? result.FailureCode
            : result.RejectionCode;
        return !string.IsNullOrWhiteSpace(resetCode) &&
               CachedProfileResetCodes.Contains(resetCode);
    }

    private static string ResolveClientName(string? overrideClientName, DeviceAppProfile? deviceAppProfile)
    {
        if (!string.IsNullOrWhiteSpace(overrideClientName))
        {
            return overrideClientName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(deviceAppProfile?.App?.AppName))
        {
            return deviceAppProfile.App.AppName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(deviceAppProfile?.App?.AppId))
        {
            return deviceAppProfile.App.AppId.Trim();
        }

        return Assembly.GetEntryAssembly()?.GetName().Name?.Trim()
               ?? "Ansight App";
    }
}

/// <summary>
/// Result returned when attempting to open a live pairing session.
/// </summary>
/// <param name="Success"><see langword="true"/> when the session was opened successfully.</param>
/// <param name="Accepted"><see langword="true"/> when the host accepted the connection request.</param>
/// <param name="Message">Human-readable status message for the attempt.</param>
/// <param name="HostAddress">Resolved host address that was used for the connection attempt.</param>
/// <param name="ConnectResponse">Handshake response returned by the host, when available.</param>
/// <param name="FailureCode">Optional machine-readable failure code for transport or setup failures.</param>
public sealed record OpenSessionResult(
    bool Success,
    bool Accepted,
    string Message,
    IPAddress? HostAddress,
    ConnectResponse? ConnectResponse,
    string? FailureCode = null)
{
    /// <summary>
    /// Creates a failed session result.
    /// </summary>
    /// <param name="message">Human-readable failure message.</param>
    /// <param name="failureCode">Optional machine-readable failure code.</param>
    /// <returns>A failed open-session result.</returns>
    public static OpenSessionResult FromFailure(string message, string? failureCode = null) => new(false, false, message, null, null, failureCode);

    /// <summary>
    /// Gets the host-provided rejection code when the connection request was rejected.
    /// </summary>
    public string? RejectionCode => Accepted ? null : ConnectResponse?.Reason;

    /// <summary>
    /// Gets the best available human-readable rejection reason when the connection request was rejected.
    /// </summary>
    public string? RejectionReason => Accepted
        ? null
        : FirstNonEmpty(ConnectResponse?.ReasonMessage, ConnectResponse?.Message, Message);

    /// <summary>
    /// Creates a result representing a host-level rejection of the connection request.
    /// </summary>
    /// <param name="hostAddress">Host address that replied to the request.</param>
    /// <param name="connectResponse">Handshake response returned by the host.</param>
    /// <returns>A rejected open-session result.</returns>
    public static OpenSessionResult FromRejected(IPAddress hostAddress, ConnectResponse connectResponse) =>
        new(false, false, FirstNonEmpty(connectResponse.ReasonMessage, connectResponse.Message, "Host rejected the connection request."), hostAddress, connectResponse, null);

    /// <summary>
    /// Creates a successful session result.
    /// </summary>
    /// <param name="message">Human-readable success message.</param>
    /// <param name="hostAddress">Host address used for the live session.</param>
    /// <param name="connectResponse">Handshake response returned by the host.</param>
    /// <returns>A successful open-session result.</returns>
    public static OpenSessionResult FromSuccess(
        string message,
        IPAddress hostAddress,
        ConnectResponse connectResponse) =>
        new(true, true, message, hostAddress, connectResponse, null);

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

/// <summary>
/// Lightweight success/failure result returned by pairing transport and streaming operations.
/// </summary>
/// <param name="Success"><see langword="true"/> when the operation completed successfully.</param>
/// <param name="Message">Human-readable result message.</param>
public sealed record OperationResult(bool Success, string Message)
{
    /// <summary>
    /// Creates a successful operation result.
    /// </summary>
    /// <param name="message">Human-readable success message.</param>
    /// <returns>A successful operation result.</returns>
    public static OperationResult FromSuccess(string message) => new(true, message);

    /// <summary>
    /// Creates a failed operation result.
    /// </summary>
    /// <param name="message">Human-readable failure message.</param>
    /// <returns>A failed operation result.</returns>
    public static OperationResult FromFailure(string message) => new(false, message);
}

/// <summary>
/// Result returned after processing a raw pairing tool-protocol message.
/// </summary>
/// <param name="Success"><see langword="true"/> when the incoming message was processed successfully.</param>
/// <param name="Message">Human-readable processing result.</param>
/// <param name="ResponseJson">Tool-protocol response JSON to send back to the host, when applicable.</param>
public sealed record ToolProtocolProcessResult(bool Success, string Message, string? ResponseJson)
{
    /// <summary>
    /// Creates a successful tool-protocol processing result.
    /// </summary>
    /// <param name="responseJson">Response JSON to send back to the host.</param>
    /// <returns>A successful processing result.</returns>
    public static ToolProtocolProcessResult FromSuccess(string responseJson)
        => new(true, "Tool protocol message processed.", responseJson);

    /// <summary>
    /// Creates a failed tool-protocol processing result.
    /// </summary>
    /// <param name="message">Human-readable failure message.</param>
    /// <returns>A failed processing result.</returns>
    public static ToolProtocolProcessResult FromFailure(string message)
        => new(false, message, null);
}
