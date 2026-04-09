namespace Ansight;

/// <summary>
/// Result returned by runtime-owned host connection actions.
/// </summary>
public sealed record HostConnectionResult(
    bool Success,
    string Message,
    HostConnectionActionKind Kind = HostConnectionActionKind.None,
    HostConnectionSource Source = HostConnectionSource.None,
    string? ReasonCode = null)
{
    public static HostConnectionResult FromSuccess(
        string message,
        HostConnectionActionKind kind = HostConnectionActionKind.None,
        HostConnectionSource source = HostConnectionSource.None,
        string? reasonCode = null)
        => new(true, message, kind, source, reasonCode);

    public static HostConnectionResult FromFailure(
        string message,
        HostConnectionActionKind kind = HostConnectionActionKind.None,
        HostConnectionSource source = HostConnectionSource.None,
        string? reasonCode = null)
        => new(false, message, kind, source, reasonCode);
}
