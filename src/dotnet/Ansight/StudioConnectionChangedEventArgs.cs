namespace Ansight;

/// <summary>
/// Raised when the runtime-owned host pairing status changes.
/// </summary>
public sealed class StudioConnectionChangedEventArgs : EventArgs
{
    public StudioConnectionChangedEventArgs(
        StudioConnectionStatus status,
        StudioConnectionCapabilities capabilities)
    {
        Status = status ?? throw new ArgumentNullException(nameof(status));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public StudioConnectionStatus Status { get; }

    public StudioConnectionCapabilities Capabilities { get; }
}
