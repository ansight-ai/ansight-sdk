namespace Ansight.Annotations;

/// <summary>
/// Capture and delivery settings for the opt-in annotation feature.
/// </summary>
public sealed class AnnotationOptions
{
    public bool CaptureScreenshot { get; internal set; } = true;

    public bool CaptureVisualTrees { get; internal set; } = true;

    public bool IncludeVisualTreeProperties { get; internal set; }

    public int VisualTreeMaxDepth { get; internal set; } = 30;

    public int VisualTreeMaxNodes { get; internal set; } = 1500;

    public int MaximumVisualTreeBytes { get; internal set; } = 4 * 1024 * 1024;

    public TimeSpan VisualTreeProviderTimeout { get; internal set; } = TimeSpan.FromSeconds(2);

    public int ScreenshotQuality { get; internal set; } = 85;

    public int ScreenshotMaxWidth { get; internal set; } = 1440;

    public long MaximumArtifactBytes { get; internal set; } = 10 * 1024 * 1024;

    public string? OutboxDirectory { get; internal set; }

    public IAnnotationEvidencePolicy EvidencePolicy { get; internal set; } = PermitAllAnnotationEvidencePolicy.Instance;

    internal IReadOnlyList<IAnnotationCaptureHook> Hooks { get; set; } = Array.Empty<IAnnotationCaptureHook>();

    internal AnnotationOptions Clone()
    {
        return new AnnotationOptions
        {
            CaptureScreenshot = CaptureScreenshot,
            CaptureVisualTrees = CaptureVisualTrees,
            IncludeVisualTreeProperties = IncludeVisualTreeProperties,
            VisualTreeMaxDepth = VisualTreeMaxDepth,
            VisualTreeMaxNodes = VisualTreeMaxNodes,
            MaximumVisualTreeBytes = MaximumVisualTreeBytes,
            VisualTreeProviderTimeout = VisualTreeProviderTimeout,
            ScreenshotQuality = ScreenshotQuality,
            ScreenshotMaxWidth = ScreenshotMaxWidth,
            MaximumArtifactBytes = MaximumArtifactBytes,
            OutboxDirectory = OutboxDirectory,
            EvidencePolicy = EvidencePolicy,
            Hooks = Hooks.ToArray()
        };
    }

    internal AnnotationOptions Normalize()
    {
        var normalized = Clone();
        normalized.VisualTreeMaxDepth = Math.Clamp(normalized.VisualTreeMaxDepth, 1, 64);
        normalized.VisualTreeMaxNodes = Math.Clamp(normalized.VisualTreeMaxNodes, 1, 100_000);
        normalized.MaximumVisualTreeBytes = Math.Clamp(normalized.MaximumVisualTreeBytes, 16 * 1024, 64 * 1024 * 1024);
        normalized.VisualTreeProviderTimeout = normalized.VisualTreeProviderTimeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(2)
            : normalized.VisualTreeProviderTimeout;
        normalized.ScreenshotQuality = Math.Clamp(normalized.ScreenshotQuality, 1, 100);
        normalized.ScreenshotMaxWidth = Math.Clamp(normalized.ScreenshotMaxWidth, 1, 8192);
        normalized.MaximumArtifactBytes = Math.Clamp(normalized.MaximumArtifactBytes, 1024, 256L * 1024 * 1024);
        normalized.EvidencePolicy ??= PermitAllAnnotationEvidencePolicy.Instance;
        return normalized;
    }
}
