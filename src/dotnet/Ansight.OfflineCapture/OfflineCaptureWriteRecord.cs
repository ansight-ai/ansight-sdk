namespace Ansight.OfflineCapture;

internal sealed record OfflineCaptureWriteRecord(
    OfflineCaptureWriteKind Kind,
    DateTimeOffset CapturedAtUtc,
    string JsonLine,
    TaskCompletionSource? FlushCompletion = null,
    string? FileName = null);
