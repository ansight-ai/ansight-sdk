namespace Ansight;

/// <summary>
/// Result returned by runtime-owned Studio connection actions.
/// </summary>
public sealed record StudioConnectionResult(
    bool Success,
    string Message,
    StudioConnectionActionKind Kind = StudioConnectionActionKind.None,
    StudioConnectionSource Source = StudioConnectionSource.None,
    string? ReasonCode = null)
{
    public static StudioConnectionResult FromSuccess(
        string message,
        StudioConnectionActionKind kind = StudioConnectionActionKind.None,
        StudioConnectionSource source = StudioConnectionSource.None,
        string? reasonCode = null)
        => new(true, message, kind, source, reasonCode);

    public static StudioConnectionResult FromFailure(
        string message,
        StudioConnectionActionKind kind = StudioConnectionActionKind.None,
        StudioConnectionSource source = StudioConnectionSource.None,
        string? reasonCode = null)
        => new(false, message, kind, source, reasonCode);
}
