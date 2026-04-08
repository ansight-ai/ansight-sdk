using Ansight.Pairing;

namespace Ansight;

/// <summary>
/// Result returned by runtime-owned host connection actions.
/// </summary>
public sealed record HostConnectionActionResult(
    bool Success,
    string Message,
    OpenSessionResult? SessionResult = null,
    StudioConnectionActionKind Kind = StudioConnectionActionKind.None,
    StudioConnectionSource Source = StudioConnectionSource.None,
    string? ReasonCode = null)
{
    public static HostConnectionActionResult FromSuccess(
        string message,
        OpenSessionResult? sessionResult = null,
        StudioConnectionActionKind kind = StudioConnectionActionKind.None,
        StudioConnectionSource source = StudioConnectionSource.None,
        string? reasonCode = null)
        => new(true, message, sessionResult, kind, source, reasonCode);

    public static HostConnectionActionResult FromFailure(
        string message,
        OpenSessionResult? sessionResult = null,
        StudioConnectionActionKind kind = StudioConnectionActionKind.None,
        StudioConnectionSource source = StudioConnectionSource.None,
        string? reasonCode = null)
        => new(false, message, sessionResult, kind, source, reasonCode);
}
