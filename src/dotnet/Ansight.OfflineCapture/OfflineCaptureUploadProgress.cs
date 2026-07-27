namespace Ansight.OfflineCapture;

/// <summary>
/// Describes the current stage and byte progress of an offline capture upload.
/// </summary>
public sealed record OfflineCaptureUploadProgress(
    OfflineCaptureUploadStage Stage,
    long BytesTransferred,
    long TotalBytes,
    int Attempt);
