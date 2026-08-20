namespace Ansight.OfflineCapture;

/// <summary>
/// Configures offline capture storage, activation, export, and optional runtime-capture overrides.
/// </summary>
public sealed class OfflineCaptureOptions
{
    /// <summary>
    /// Default maximum bytes retained for one capture session.
    /// </summary>
    public const long DefaultMaximumSessionBytes = 128L * 1024L * 1024L;

    /// <summary>
    /// Default maximum bytes retained across all capture sessions.
    /// </summary>
    public const long DefaultMaximumRetainedBytes = 512L * 1024L * 1024L;

    /// <summary>
    /// Default duration for one append-only segment file.
    /// </summary>
    public const int DefaultSegmentDurationSeconds = 10;

    /// <summary>
    /// Default bounded queue size between capture callbacks and disk writers.
    /// </summary>
    public const int DefaultMaximumQueuedRecords = 4096;

    /// <summary>
    /// Root folder used by offline capture. The folder itself should normally be named ".ansight".
    /// </summary>
    public string? RootDirectory { get; set; }

    /// <summary>
    /// Automatic activation mode persisted in the .ansight settings file.
    /// </summary>
    public OfflineCaptureActivationMode ActivationMode { get; set; } = OfflineCaptureActivationMode.Disabled;

    /// <summary>
    /// Optional rolling retention override for offline files. When null, offline capture uses the runtime retention period.
    /// </summary>
    public TimeSpan? RetentionWindowOverride { get; set; }

    /// <summary>
    /// Maximum bytes retained for one capture session.
    /// </summary>
    public long MaximumSessionBytes { get; set; } = DefaultMaximumSessionBytes;

    /// <summary>
    /// Maximum bytes retained across all capture sessions.
    /// </summary>
    public long MaximumRetainedBytes { get; set; } = DefaultMaximumRetainedBytes;

    /// <summary>
    /// Approximate duration for each append-only telemetry/input segment file.
    /// </summary>
    public TimeSpan SegmentDuration { get; set; } = TimeSpan.FromSeconds(DefaultSegmentDurationSeconds);

    /// <summary>
    /// Optional JPEG capture enable override. When null, offline capture follows whether runtime session JPEG capture is configured.
    /// </summary>
    public bool? SessionJpegCaptureEnabledOverride { get; set; }

    /// <summary>
    /// Optional JPEG capture settings override. When null, offline capture uses the runtime session JPEG capture settings.
    /// </summary>
    public SessionJpegCaptureOptions? SessionJpegCaptureOverride { get; set; }

    /// <summary>
    /// Bounded in-memory queue size used between capture callbacks and disk writers.
    /// </summary>
    public int MaximumQueuedRecords { get; set; } = DefaultMaximumQueuedRecords;

    /// <summary>
    /// Creates a deep copy of the options.
    /// </summary>
    public OfflineCaptureOptions Clone()
    {
        return new OfflineCaptureOptions
        {
            RootDirectory = RootDirectory,
            ActivationMode = ActivationMode,
            RetentionWindowOverride = RetentionWindowOverride,
            MaximumSessionBytes = MaximumSessionBytes,
            MaximumRetainedBytes = MaximumRetainedBytes,
            SegmentDuration = SegmentDuration,
            SessionJpegCaptureEnabledOverride = SessionJpegCaptureEnabledOverride,
            SessionJpegCaptureOverride = SessionJpegCaptureOverride is null
                ? null
                : new SessionJpegCaptureOptions
                {
                    IntervalMilliseconds = SessionJpegCaptureOverride.IntervalMilliseconds,
                    Quality = SessionJpegCaptureOverride.Quality,
                    MaxWidth = SessionJpegCaptureOverride.MaxWidth,
                    CaptureGpuBackedSurfaces = SessionJpegCaptureOverride.CaptureGpuBackedSurfaces,
                    CaptureKeyboardPresence = SessionJpegCaptureOverride.CaptureKeyboardPresence,
                    Mode = SessionJpegCaptureOverride.Mode
                },
            MaximumQueuedRecords = MaximumQueuedRecords
        };
    }

    internal OfflineCaptureOptions Normalize()
    {
        var normalized = Clone();
        normalized.RootDirectory = ResolveRootDirectory(normalized.RootDirectory);

        if (normalized.ActivationMode == OfflineCaptureActivationMode.Immediate)
        {
            normalized.ActivationMode = OfflineCaptureActivationMode.Disabled;
        }

        if (normalized.RetentionWindowOverride.HasValue &&
            normalized.RetentionWindowOverride.Value < TimeSpan.FromSeconds(1))
        {
            normalized.RetentionWindowOverride = TimeSpan.FromSeconds(1);
        }

        if (normalized.MaximumSessionBytes < 1024 * 1024)
        {
            normalized.MaximumSessionBytes = 1024 * 1024;
        }

        if (normalized.MaximumRetainedBytes < normalized.MaximumSessionBytes)
        {
            normalized.MaximumRetainedBytes = normalized.MaximumSessionBytes;
        }

        if (normalized.SegmentDuration < TimeSpan.FromSeconds(1))
        {
            normalized.SegmentDuration = TimeSpan.FromSeconds(1);
        }

        if (normalized.SessionJpegCaptureOverride is not null)
        {
            if (normalized.SessionJpegCaptureOverride.IntervalMilliseconds < 250)
            {
                normalized.SessionJpegCaptureOverride.IntervalMilliseconds = 250;
            }

            normalized.SessionJpegCaptureOverride.Quality = Math.Clamp(
                normalized.SessionJpegCaptureOverride.Quality,
                1,
                100);
            if (normalized.SessionJpegCaptureOverride.MaxWidth is <= 0)
            {
                normalized.SessionJpegCaptureOverride.MaxWidth = null;
            }
            else if (normalized.SessionJpegCaptureOverride.MaxWidth > 8192)
            {
                normalized.SessionJpegCaptureOverride.MaxWidth = 8192;
            }
        }

        if (normalized.MaximumQueuedRecords < 128)
        {
            normalized.MaximumQueuedRecords = 128;
        }

        return normalized;
    }

    internal static string ResolveRootDirectory(string? rootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(rootDirectory))
        {
            return Path.GetFullPath(rootDirectory);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = string.IsNullOrWhiteSpace(localAppData)
            ? AppContext.BaseDirectory
            : localAppData;
        return Path.Combine(baseDirectory, "Ansight", ".ansight");
    }
}
