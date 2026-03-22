namespace Ansight;

/// <summary>
/// Structured progress update emitted during host pairing operations.
/// </summary>
public sealed record HostPairingProgressUpdate(
    HostPairingProgressKind Kind,
    string Message,
    bool IsVerbose = false,
    HostPairingSource Source = HostPairingSource.None,
    string? ReasonCode = null);
