namespace Ansight.OfflineCapture;

/// <summary>
/// Identifies a capture accepted by an Ansight team.
/// </summary>
public sealed record OfflineCaptureUploadResult(
    string UploadId,
    string SessionId,
    Uri? SessionUrl,
    long ArchiveByteSize,
    string ArchiveSha256);
