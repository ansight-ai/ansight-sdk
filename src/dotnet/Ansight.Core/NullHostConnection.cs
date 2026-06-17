using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class NullHostConnection : IHostConnection
{
    internal static NullHostConnection Instance { get; } = new();

    private static readonly HostConnectionStatus status = new(
        IsRuntimeActive: false,
        IsConnected: false,
        ConnectionState: HostConnectionState.Disconnected,
        HasCachedSession: false,
        HasSavedConfig: false,
        HasBundledConfig: false,
        SummaryKind: HostConnectionSummaryKind.RuntimeUnavailable,
        SummaryMessage: "Ansight runtime is not initialized.");

    private static readonly HostConnectionCapabilities capabilities = new(
        CanConnectUsingSavedConfig: false,
        CanConnectUsingBundledConfig: false,
        CanChooseConfigFile: false,
        CanScanConfigQrCode: false,
        CanClearSavedConfigs: false);

    public bool HasSavedConfig => false;

    public bool IsConnected => false;

    public HostConnectionStatus Status => status;

    public HostConnectionCapabilities Capabilities => capabilities;

    public event EventHandler<HostConnectionChangedEventArgs>? StatusChanged
    {
        add { }
        remove { }
    }

    private static string StatusSummary => status.SummaryMessage;

    public Task<HostConnectionCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(capabilities);

    public Task<HostConnectionResult> NotifyConfigChangedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HostConnectionResult.FromFailure(StatusSummary, HostConnectionActionKind.NotifyConfigChanged));

    public bool TryParseConfigDocument(string payload, out PairingConfigDocument? config, out string error)
    {
        config = null;
        error = StatusSummary;
        return false;
    }

    public Task<HostConnectionResult> ConnectAsync(
        HostConnectionRequest? request = null,
        string? clientName = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(HostConnectionResult.FromFailure(
            StatusSummary,
            request?.Kind is HostConnectionRequestKind.Auto or null
                ? HostConnectionActionKind.AutoConnect
                : HostConnectionActionKind.Connect));

    public Task<HostConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HostConnectionResult.FromFailure(StatusSummary, HostConnectionActionKind.Disconnect));

    public Task<OperationResult> SendClientLogAsync(
        string logLine,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.FromFailure(StatusSummary));

    public HostConnectionResult ClearSavedConfigs()
        => HostConnectionResult.FromFailure(StatusSummary, HostConnectionActionKind.ClearSavedConfigs);
}
