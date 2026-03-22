namespace Ansight;

internal sealed class NullHostPairing : IHostPairing
{
    internal static NullHostPairing Instance { get; } = new();

    private static readonly HostPairingStatusSnapshot status = new(
        IsRuntimeActive: false,
        IsConnected: false,
        ConnectionState: HostConnectionState.Disconnected,
        HasCachedProfile: false,
        HasPreferredProfile: false,
        HasBundledProfile: false,
        SummaryKind: HostPairingSummaryKind.RuntimeUnavailable,
        SummaryMessage: "Ansight runtime is not initialized.");

    private static readonly HostPairingCapabilities capabilities = new(
        CanConnectUsingStored: false,
        CanConnectUsingBundled: false,
        CanClearProfiles: false,
        CanUseQrPayloadWithBaseProfile: false);

    public bool HasPreferredProfile => false;

    public bool IsConnected => false;

    public HostPairingStatusSnapshot Status => status;

    public HostPairingCapabilities Capabilities => capabilities;

    public event EventHandler<HostPairingStatusChangedEventArgs>? StatusChanged
    {
        add { }
        remove { }
    }

    private static string StatusSummary => status.SummaryMessage;

    public Task<HostPairingCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(capabilities);
    }

    public bool CanReadPayload(HostPairingPayloadReadKind kind)
    {
        return false;
    }

    public Task<HostPairingActionResult> AutoConnectAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(
            StatusSummary,
            HostPairingActionKind.AutoConnect));
    }

    public Task<HostPairingActionResult> ConnectUsingStoredProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(
            StatusSummary,
            HostPairingActionKind.ConnectUsingStoredProfile));
    }

    public Task<HostPairingActionResult> ConnectUsingBundledProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(
            StatusSummary,
            HostPairingActionKind.ConnectUsingBundledProfile));
    }

    public Task<HostPairingActionResult> ConnectFromPayloadAsync(
        string payload,
        string? sourceDescription = null,
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(
            StatusSummary,
            HostPairingActionKind.ConnectFromPayload));
    }

    public Task<HostPairingActionResult> ConnectFromPayloadReaderAsync(
        HostPairingPayloadReadRequest request,
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(
            StatusSummary,
            HostPairingActionKind.ConnectFromPayload,
            HostPairingSource.PayloadReader));
    }

    public Task<HostPairingActionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(
            StatusSummary,
            HostPairingActionKind.Disconnect));
    }

    public HostPairingActionResult ClearStoredProfiles()
    {
        return HostPairingActionResult.FromFailure(
            StatusSummary,
            HostPairingActionKind.ClearStoredProfiles);
    }
}
