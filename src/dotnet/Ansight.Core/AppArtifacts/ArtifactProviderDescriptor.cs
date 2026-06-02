namespace Ansight.Artifacts;

/// <summary>
/// Describes an artifact provider registered by the app.
/// </summary>
/// <param name="Id">Stable provider identifier used in artifact requests.</param>
/// <param name="Name">Human-readable provider name.</param>
/// <param name="Description">Human-readable description of the provider.</param>
/// <param name="Category">High-level category used to group providers in clients.</param>
public sealed record ArtifactProviderDescriptor(
    string Id,
    string Name,
    string Description,
    string Category)
{
    /// <summary>
    /// Search or grouping tags for this provider.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// App-defined provider metadata. Values are string-only to keep the wire contract stable.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
