using Ansight.Pairing;

namespace Ansight;

/// <summary>
/// Result returned by runtime-owned host connection actions.
/// </summary>
public sealed record HostConnectionActionResult(
    bool Success,
    string Message,
    OpenSessionResult? SessionResult = null,
    HostPairingActionKind Kind = HostPairingActionKind.None,
    HostPairingSource Source = HostPairingSource.None,
    string? ReasonCode = null)
{
    public static HostConnectionActionResult FromSuccess(
        string message,
        OpenSessionResult? sessionResult = null,
        HostPairingActionKind kind = HostPairingActionKind.None,
        HostPairingSource source = HostPairingSource.None,
        string? reasonCode = null)
        => new(true, message, sessionResult, kind, source, reasonCode);

    public static HostConnectionActionResult FromFailure(
        string message,
        OpenSessionResult? sessionResult = null,
        HostPairingActionKind kind = HostPairingActionKind.None,
        HostPairingSource source = HostPairingSource.None,
        string? reasonCode = null)
        => new(false, message, sessionResult, kind, source, reasonCode);
}
