namespace Ansight.Artifacts;

using Ansight.Tools;

/// <summary>
/// Describes an artifact that can be requested from a provider.
/// </summary>
/// <param name="Id">Stable artifact identifier unique within the provider.</param>
/// <param name="Name">Human-readable artifact name.</param>
/// <param name="Description">Human-readable artifact description.</param>
/// <param name="Kind">Provider-defined artifact kind such as text, image, log, trace, or report.</param>
/// <param name="Category">High-level category used to group artifacts in clients.</param>
/// <param name="Content">Content shape and supported formats.</param>
/// <param name="ArgumentsSchema">Schema for provider-specific request arguments.</param>
/// <param name="Policy">Policy required to request the artifact.</param>
public sealed record ArtifactDefinition(
    string Id,
    string Name,
    string Description,
    string Kind,
    string Category,
    ArtifactContentDescriptor Content,
    ToolSchema ArgumentsSchema,
    ToolPolicy Policy)
{
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
