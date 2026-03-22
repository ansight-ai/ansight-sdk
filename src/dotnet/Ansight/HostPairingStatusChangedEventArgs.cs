namespace Ansight;

/// <summary>
/// Raised when the runtime-owned host pairing status changes.
/// </summary>
public sealed class HostPairingStatusChangedEventArgs : EventArgs
{
    public HostPairingStatusChangedEventArgs(
        HostPairingStatusSnapshot status,
        HostPairingCapabilities capabilities)
    {
        Status = status ?? throw new ArgumentNullException(nameof(status));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public HostPairingStatusSnapshot Status { get; }

    public HostPairingCapabilities Capabilities { get; }
}
