namespace Ansight.OfflineCapture;

internal sealed class OfflineCaptureEffectiveOptions
{
    public required string RootDirectory { get; init; }

    public required TimeSpan RetentionWindow { get; init; }

    public required long MaximumSessionBytes { get; init; }

    public required long MaximumRetainedBytes { get; init; }

    public required TimeSpan SegmentDuration { get; init; }

    public required int MaximumQueuedRecords { get; init; }

    public SessionJpegCaptureOptions? SessionJpegCapture { get; init; }
}
