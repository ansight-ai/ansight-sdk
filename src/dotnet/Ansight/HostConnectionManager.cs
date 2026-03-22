using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class HostConnectionManager : IHostConnection, IHostAutoProbeSessionClient, IDisposable
{
    private readonly RuntimeImpl runtime;
    private readonly HostAutoProbeOptions autoProbeOptions;
    private readonly IHostConnectionSessionClient sessionClient;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Lock statusGate = new();
    private readonly HostAutoProbeCoordinator? autoProbeCoordinator;
    private OpenSessionResult? activeSessionResult;
    private HostConnectionState state = HostConnectionState.Disconnected;
    private string statusSummary;
    private bool disposed;

    internal HostConnectionManager(
        RuntimeImpl runtime,
        HostAutoProbeOptions autoProbeOptions,
        IHostConnectionSessionClient? sessionClient = null)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.autoProbeOptions = autoProbeOptions?.Clone() ?? throw new ArgumentNullException(nameof(autoProbeOptions));
        this.sessionClient = sessionClient ?? new PairingSessionClient();
        statusSummary = BuildDisconnectedSummary();
        this.sessionClient.SessionClosed += HandleSessionClosed;

        if (this.autoProbeOptions.Enabled)
        {
            autoProbeCoordinator = new HostAutoProbeCoordinator(this.autoProbeOptions, this);
        }
    }

    public HostConnectionState State
    {
        get
        {
            lock (statusGate)
            {
                return state;
            }
        }
    }

    public bool IsConnected => sessionClient.IsSessionOpen;

    bool IHostAutoProbeSessionClient.IsConnected => IsConnected;

    public bool HasCachedProfile => sessionClient.HasCachedPairingProfile;

    bool IHostAutoProbeSessionClient.HasCachedProfile => HasCachedProfile;

    public string StatusSummary
    {
        get
        {
            lock (statusGate)
            {
                return statusSummary;
            }
        }
    }

    internal DateTimeOffset? LastDisconnectedAtUtc { get; private set; }

    DateTimeOffset? IHostAutoProbeSessionClient.LastDisconnectedAtUtc => LastDisconnectedAtUtc;

    public event EventHandler<HostConnectionStatusChangedEventArgs>? StatusChanged;

    public bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error)
    {
        return sessionClient.TryParseAndValidateDocument(configJson, out document, out error);
    }

    public Task<HostConnectionActionResult> ConnectAsync(
        ParsedPairingDocument document,
        string? clientName = null,
        PairingConnectionOptions? connectionOptions = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return ConnectCoreAsync(
            reuseExistingConnection: false,
            $"Connecting to Ansight host using {DescribeConnectionTarget(document)}.",
            (resolvedClientName, effectiveProgress, effectiveCancellationToken) => sessionClient.OpenSessionAsync(
                document,
                resolvedClientName,
                connectionOptions,
                effectiveProgress,
                effectiveCancellationToken),
            clientName,
            progress,
            cancellationToken);
    }

    public Task<HostConnectionActionResult> ConnectUsingCachedProfileAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ConnectUsingCachedProfileCoreAsync(clientName, progress, cancellationToken);
    }

    Task<HostConnectionActionResult> IHostAutoProbeSessionClient.ConnectUsingCachedProfileAsync(
        string? clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
        => ConnectUsingCachedProfileCoreAsync(clientName, progress, cancellationToken);

    public async Task<HostConnectionActionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await sessionClient.CloseSessionAsync(cancellationToken);
            activeSessionResult = null;
            SetStatus(HostConnectionState.Disconnected, runtime.IsActive ? BuildDisconnectedSummary() : BuildInactiveSummary());
            return result.Success
                ? HostConnectionActionResult.FromSuccess(result.Message)
                : HostConnectionActionResult.FromFailure(result.Message);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            activeSessionResult = null;
            SetStatus(HostConnectionState.Disconnected, runtime.IsActive ? BuildDisconnectedSummary() : BuildInactiveSummary());
            return HostConnectionActionResult.FromFailure($"Failed to disconnect from the Ansight host: {ex.Message}");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public HostConnectionActionResult ClearCachedProfile()
    {
        try
        {
            sessionClient.ClearCachedPairingProfile();
            if (!IsConnected)
            {
                SetStatus(HostConnectionState.Disconnected, BuildDisconnectedSummary());
            }

            return HostConnectionActionResult.FromSuccess("Cleared the cached Ansight host pairing profile.");
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            return HostConnectionActionResult.FromFailure($"Failed to clear the cached Ansight host pairing profile: {ex.Message}");
        }
    }

    internal void OnRuntimeActivated()
    {
        if (!sessionClient.IsSessionOpen)
        {
            SetStatus(HostConnectionState.Disconnected, BuildDisconnectedSummary());
        }

        autoProbeCoordinator?.OnActivated();
    }

    internal void OnRuntimeDeactivated()
    {
        if (autoProbeCoordinator is not null)
        {
            autoProbeCoordinator.OnDeactivated();
            return;
        }

        _ = DisconnectAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        autoProbeCoordinator?.Dispose();
        sessionClient.SessionClosed -= HandleSessionClosed;
        sessionClient.Dispose();
        operationGate.Dispose();
    }

    private Task<HostConnectionActionResult> ConnectUsingCachedProfileCoreAsync(
        string? clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return ConnectCoreAsync(
            reuseExistingConnection: true,
            "Connecting to the cached Ansight host pairing profile.",
            (resolvedClientName, effectiveProgress, effectiveCancellationToken) => sessionClient.OpenCachedSessionAsync(
                resolvedClientName,
                effectiveProgress,
                effectiveCancellationToken),
            clientName,
            progress,
            cancellationToken);
    }

    private async Task<HostConnectionActionResult> ConnectCoreAsync(
        bool reuseExistingConnection,
        string connectingSummary,
        Func<string, IProgress<string>?, CancellationToken, Task<OpenSessionResult>> openAsync,
        string? clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!runtime.IsActive)
        {
            var inactiveMessage = "Activate Ansight before connecting to a host.";
            SetStatus(HostConnectionState.Disconnected, inactiveMessage);
            return HostConnectionActionResult.FromFailure(inactiveMessage);
        }

        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (reuseExistingConnection && sessionClient.IsSessionOpen)
            {
                return HostConnectionActionResult.FromSuccess(StatusSummary, activeSessionResult);
            }

            var resolvedClientName = sessionClient.ResolveClientName(
                string.IsNullOrWhiteSpace(clientName) ? autoProbeOptions.ClientName : clientName);

            SetStatus(HostConnectionState.Connecting, connectingSummary);
            var openResult = await openAsync(resolvedClientName, progress, cancellationToken);
            if (!openResult.Success)
            {
                activeSessionResult = null;
                SetStatus(HostConnectionState.Disconnected, ResolveFailureMessage(openResult));
                return HostConnectionActionResult.FromFailure(ResolveFailureMessage(openResult), openResult);
            }

            activeSessionResult = openResult;
            var metricsResult = await sessionClient.StartMetricsStreamingAsync(
                runtime.DataSink,
                progress,
                cancellationToken);
            if (!metricsResult.Success)
            {
                await sessionClient.CloseSessionAsync(CancellationToken.None);
                activeSessionResult = null;
                SetStatus(HostConnectionState.Disconnected, metricsResult.Message);
                return HostConnectionActionResult.FromFailure(metricsResult.Message, openResult);
            }

            LastDisconnectedAtUtc = null;
            SetStatus(HostConnectionState.Connected, BuildConnectedSummary(openResult));
            return HostConnectionActionResult.FromSuccess("Connected to the Ansight host.", openResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            try
            {
                await sessionClient.CloseSessionAsync(CancellationToken.None);
            }
            catch
            {
                // Best effort.
            }

            activeSessionResult = null;
            var message = $"Connection failed: {ex.Message}";
            SetStatus(HostConnectionState.Disconnected, message);
            return HostConnectionActionResult.FromFailure(message);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private void HandleSessionClosed(object? sender, EventArgs e)
    {
        activeSessionResult = null;
        LastDisconnectedAtUtc = DateTimeOffset.UtcNow;
        SetStatus(
            HostConnectionState.Disconnected,
            !runtime.IsActive
                ? BuildInactiveSummary()
                : runtime.IsActive && autoProbeOptions.Enabled && sessionClient.HasCachedPairingProfile
                ? "Ansight host disconnected. Auto-probe will retry using the cached pairing profile."
                : BuildDisconnectedSummary());
    }

    private void SetStatus(HostConnectionState nextState, string nextStatusSummary)
    {
        EventHandler<HostConnectionStatusChangedEventArgs>? statusChanged;
        HostConnectionStatusChangedEventArgs? args = null;

        lock (statusGate)
        {
            var normalizedSummary = nextStatusSummary?.Trim() ?? string.Empty;
            if (state == nextState && string.Equals(statusSummary, normalizedSummary, StringComparison.Ordinal))
            {
                return;
            }

            state = nextState;
            statusSummary = normalizedSummary;
            statusChanged = StatusChanged;
            args = new HostConnectionStatusChangedEventArgs(
                state,
                IsConnected,
                HasCachedProfile,
                statusSummary);
        }

        statusChanged?.Invoke(this, args);
    }

    private string BuildDisconnectedSummary()
    {
        return sessionClient.HasCachedPairingProfile
            ? "No Ansight host session is connected. A cached pairing profile is available."
            : "No Ansight host session is connected.";
    }

    private static string BuildInactiveSummary()
    {
        return "Activate Ansight before connecting to a host.";
    }

    private static string BuildConnectedSummary(OpenSessionResult openResult)
    {
        var hostName = openResult.ConnectResponse?.HostName;
        if (string.IsNullOrWhiteSpace(hostName))
        {
            hostName = "Ansight host";
        }

        if (openResult.HostAddress is not null)
        {
            return $"Streaming live metrics to {hostName} at {openResult.HostAddress}.";
        }

        return $"Streaming live metrics to {hostName}.";
    }

    private static string ResolveFailureMessage(OpenSessionResult openResult)
    {
        if (!openResult.Accepted && !string.IsNullOrWhiteSpace(openResult.RejectionReason))
        {
            return openResult.RejectionReason;
        }

        return string.IsNullOrWhiteSpace(openResult.Message)
            ? "Unable to connect to the Ansight host."
            : openResult.Message;
    }

    private static string DescribeConnectionTarget(ParsedPairingDocument document)
    {
        var hostName = document.DiscoveryHint?.HostName?.Trim();
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            return hostName;
        }

        hostName = document.Config.Host.HostName?.Trim();
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            return hostName;
        }

        return "the selected pairing profile";
    }
}
