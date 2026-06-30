namespace Ansight.OfflineCapture;

/// <summary>
/// Describes an offline capture session on disk.
/// </summary>
public sealed record OfflineCaptureSessionInfo(
    string SessionId,
    string DirectoryPath,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    long SizeBytes,
    bool IsActive);
