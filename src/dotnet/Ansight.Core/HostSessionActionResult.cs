using Ansight.Pairing;

namespace Ansight;

/// <summary>
/// Result returned by runtime-owned host connection actions.
/// </summary>
public sealed record HostSessionActionResult(
    bool Success,
    string Message,
    OpenSessionResult? SessionResult = null,
    HostConnectionActionKind Kind = HostConnectionActionKind.None,
    HostConnectionSource Source = HostConnectionSource.None,
    string? ReasonCode = null)
{
    public static HostSessionActionResult FromSuccess(
        string message,
        OpenSessionResult? sessionResult = null,
        HostConnectionActionKind kind = HostConnectionActionKind.None,
        HostConnectionSource source = HostConnectionSource.None,
        string? reasonCode = null)
        => new(true, message, sessionResult, kind, source, reasonCode);

    public static HostSessionActionResult FromFailure(
        string message,
        OpenSessionResult? sessionResult = null,
        HostConnectionActionKind kind = HostConnectionActionKind.None,
        HostConnectionSource source = HostConnectionSource.None,
        string? reasonCode = null)
        => new(false, message, sessionResult, kind, source, reasonCode);
}
