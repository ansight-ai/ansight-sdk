namespace Ansight.OfflineCapture;

/// <summary>
/// Identifies the capture stored in an offline archive.
/// </summary>
public sealed record OfflineCaptureUploadMetadata(
    string SessionId,
    string AppId,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? StoppedAtUtc = null,
    string? SdkVersion = null);
