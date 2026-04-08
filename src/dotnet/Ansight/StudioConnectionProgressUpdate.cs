namespace Ansight;

/// <summary>
/// Structured progress update emitted during host pairing operations.
/// </summary>
public sealed record StudioConnectionProgressUpdate(
    StudioConnectionProgressKind Kind,
    string Message,
    bool IsVerbose = false,
    StudioConnectionSource Source = StudioConnectionSource.None,
    string? ReasonCode = null);
