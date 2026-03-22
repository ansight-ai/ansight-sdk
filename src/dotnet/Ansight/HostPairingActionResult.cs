namespace Ansight;

/// <summary>
/// Result returned by runtime-owned host pairing actions.
/// </summary>
public sealed record HostPairingActionResult(
    bool Success,
    string Message,
    HostPairingActionKind Kind = HostPairingActionKind.None,
    HostPairingSource Source = HostPairingSource.None,
    string? ReasonCode = null)
{
    public static HostPairingActionResult FromSuccess(
        string message,
        HostPairingActionKind kind = HostPairingActionKind.None,
        HostPairingSource source = HostPairingSource.None,
        string? reasonCode = null)
        => new(true, message, kind, source, reasonCode);

    public static HostPairingActionResult FromFailure(
        string message,
        HostPairingActionKind kind = HostPairingActionKind.None,
        HostPairingSource source = HostPairingSource.None,
        string? reasonCode = null)
        => new(false, message, kind, source, reasonCode);
}
