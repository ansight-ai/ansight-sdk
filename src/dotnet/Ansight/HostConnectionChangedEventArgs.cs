namespace Ansight;

/// <summary>
/// Raised when the runtime-owned host pairing status changes.
/// </summary>
public sealed class HostConnectionChangedEventArgs : EventArgs
{
    public HostConnectionChangedEventArgs(
        HostConnectionStatus status,
        HostConnectionCapabilities capabilities)
    {
        Status = status ?? throw new ArgumentNullException(nameof(status));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public HostConnectionStatus Status { get; }

    public HostConnectionCapabilities Capabilities { get; }
}
