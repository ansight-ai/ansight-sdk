namespace Ansight.Annotations;

/// <summary>
/// Fluent configuration for annotation capture.
/// </summary>
public sealed class AnnotationOptionsBuilder
{
    private readonly AnnotationOptions options = new();
    private readonly List<IAnnotationCaptureHook> hooks = [];

    public AnnotationOptionsBuilder WithoutScreenshot()
    {
        options.CaptureScreenshot = false;
        return this;
    }

    public AnnotationOptionsBuilder WithoutVisualTrees()
    {
        options.CaptureVisualTrees = false;
        return this;
    }

    public AnnotationOptionsBuilder IncludeVisualTreeProperties(bool include = true)
    {
        options.IncludeVisualTreeProperties = include;
        return this;
    }

    public AnnotationOptionsBuilder WithVisualTreeLimits(int maxDepth, int maxNodes, int maximumBytes = 4 * 1024 * 1024)
    {
        options.VisualTreeMaxDepth = maxDepth;
        options.VisualTreeMaxNodes = maxNodes;
        options.MaximumVisualTreeBytes = maximumBytes;
        return this;
    }

    public AnnotationOptionsBuilder WithVisualTreeProviderTimeout(TimeSpan timeout)
    {
        options.VisualTreeProviderTimeout = timeout;
        return this;
    }

    public AnnotationOptionsBuilder WithScreenshotEncoding(int quality, int maxWidth)
    {
        options.ScreenshotQuality = quality;
        options.ScreenshotMaxWidth = maxWidth;
        return this;
    }

    public AnnotationOptionsBuilder WithOutboxDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        options.OutboxDirectory = directory;
        return this;
    }

    public AnnotationOptionsBuilder WithMaximumArtifactBytes(long maximumBytes)
    {
        options.MaximumArtifactBytes = maximumBytes;
        return this;
    }

    public AnnotationOptionsBuilder WithEvidencePolicy(IAnnotationEvidencePolicy policy)
    {
        options.EvidencePolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }

    public AnnotationOptionsBuilder AddHook(IAnnotationCaptureHook hook)
    {
        hooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));
        return this;
    }

    internal AnnotationOptions Build()
    {
        options.Hooks = hooks.ToArray();
        return options.Normalize();
    }
}
