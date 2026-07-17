namespace Ansight.OfflineCapture;

internal sealed class OfflineCaptureSessionManifest
{
    public int Version { get; set; } = 1;

    public string SessionId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? StoppedAtUtc { get; set; }

    public string AppId { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string RemoteAddress { get; set; } = "offline";

    public string? ProcessSessionId { get; set; }

    public string? SdkVersion { get; set; }

    public AppLifecycleState AppState { get; set; } = AppLifecycleState.Unknown;

    public DateTimeOffset? AppStateChangedUtc { get; set; }

    public TimeSpan RetentionWindow { get; set; }

    public long MaximumSessionBytes { get; set; }

    public bool SessionJpegCaptureEnabled { get; set; }

    public int? SessionJpegCaptureIntervalMilliseconds { get; set; }

    public int? SessionJpegCaptureQuality { get; set; }

    public int? SessionJpegCaptureMaxWidth { get; set; }

    public long DroppedRecordCount { get; set; }

    public long AnnotationCount { get; set; }
}
