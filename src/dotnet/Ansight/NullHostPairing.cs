namespace Ansight;

internal sealed class NullHostPairing : IHostPairing
{
    internal static NullHostPairing Instance { get; } = new();

    public bool HasPreferredProfile => false;

    private static string StatusSummary => "Ansight runtime is not initialized.";

    public Task<HostPairingActionResult> AutoConnectAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(StatusSummary));
    }

    public Task<HostPairingActionResult> ConnectUsingStoredProfileAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(StatusSummary));
    }

    public Task<HostPairingActionResult> ConnectUsingBundledProfileAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(StatusSummary));
    }

    public Task<HostPairingActionResult> ConnectFromPayloadAsync(
        string payload,
        string? sourceDescription = null,
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HostPairingActionResult.FromFailure(StatusSummary));
    }

    public HostPairingActionResult ClearStoredProfiles()
    {
        return HostPairingActionResult.FromFailure(StatusSummary);
    }
}
