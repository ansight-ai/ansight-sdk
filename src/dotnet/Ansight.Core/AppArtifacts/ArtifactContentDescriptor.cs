namespace Ansight.Artifacts;

/// <summary>
/// Describes the content an artifact definition can produce.
/// </summary>
/// <param name="SupportedMimeTypes">MIME types the artifact may produce.</param>
public sealed record ArtifactContentDescriptor(IReadOnlyList<string> SupportedMimeTypes)
{
    /// <summary>
    /// Preferred MIME type when callers do not request a specific format.
    /// </summary>
    public string? DefaultMimeType { get; init; }

    /// <summary>
    /// Suggested file name for materializing the artifact.
    /// </summary>
    public string? SuggestedFileName { get; init; }

    /// <summary>
    /// Indicates whether the provider can produce text content.
    /// </summary>
    public bool SupportsText { get; init; }

    /// <summary>
    /// Indicates whether the provider can produce binary content.
    /// </summary>
    public bool SupportsBinary { get; init; }

    /// <summary>
    /// Indicates whether the artifact size is known before the artifact is created.
    /// </summary>
    public bool SizeKnownBeforeCreation { get; init; }

    /// <summary>
    /// Best-effort estimated artifact size in bytes.
    /// </summary>
    public long? EstimatedSizeBytes { get; init; }
}
