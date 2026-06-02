namespace Ansight.Artifacts;

/// <summary>
/// Metadata for an artifact created by an app provider.
/// </summary>
/// <param name="ArtifactId">Artifact id requested by the caller.</param>
/// <param name="ProviderId">Provider id that created the artifact.</param>
/// <param name="Name">Human-readable artifact name.</param>
/// <param name="Kind">Provider-defined artifact kind such as text, image, log, trace, or report.</param>
/// <param name="MimeType">MIME type of the artifact payload.</param>
/// <param name="FileName">Suggested file name for materializing the artifact.</param>
public sealed record ArtifactMetadata(
    string ArtifactId,
    string ProviderId,
    string Name,
    string Kind,
    string MimeType,
    string FileName)
{
    /// <summary>
    /// Optional human-readable artifact description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Artifact payload size in bytes, when known.
    /// </summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// UTC timestamp when the artifact snapshot was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Search or grouping tags for this artifact.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// App-defined artifact metadata. Values are string-only to keep the wire contract stable.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
