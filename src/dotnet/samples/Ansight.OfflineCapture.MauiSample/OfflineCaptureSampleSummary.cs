namespace Ansight.OfflineCapture.MauiSample;

public sealed record OfflineCaptureSampleSummary(
    string CaptureRoot,
    string? LastExportPath,
    OfflineCaptureSessionInfo? Session,
    bool IsCapturing,
    int RetainedBufferCount,
    int MetricSegmentCount,
    int EventSegmentCount,
    int TouchSegmentCount,
    int ScreenshotCount,
    int ScreenshotIndexSegmentCount,
    long TotalBytes)
{
    public static OfflineCaptureSampleSummary Empty(
        string captureRoot,
        string? lastExportPath,
        bool isCapturing,
        int retainedBufferCount)
    {
        return new OfflineCaptureSampleSummary(
            captureRoot,
            lastExportPath,
            null,
            isCapturing,
            retainedBufferCount,
            0,
            0,
            0,
            0,
            0,
            0);
    }
}
