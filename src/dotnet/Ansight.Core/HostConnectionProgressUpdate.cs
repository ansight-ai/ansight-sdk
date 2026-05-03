namespace Ansight;

/// <summary>
/// Structured progress update emitted during host pairing operations.
/// </summary>
public sealed record HostConnectionProgressUpdate(
    HostConnectionProgressKind Kind,
    string Message,
    bool IsVerbose = false,
    HostConnectionSource Source = HostConnectionSource.None,
    string? ReasonCode = null);
