namespace Ansight.Artifacts;

/// <summary>
/// Provides dynamically queryable and requestable app artifacts.
/// </summary>
public interface IArtifactProvider
{
    /// <summary>
    /// Provider metadata used in artifact catalogs.
    /// </summary>
    ArtifactProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Queries artifacts currently available from this provider.
    /// </summary>
    Task<IReadOnlyList<ArtifactDefinition>> QueryAsync(
        ArtifactQueryContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the requested artifact snapshot.
    /// </summary>
    Task<ArtifactResult> CreateAsync(
        ArtifactRequest request,
        CancellationToken cancellationToken = default);
}
