namespace Ansight.Artifacts;

using System.Collections;

/// <summary>
/// Immutable collection of app artifact providers.
/// </summary>
public sealed class ArtifactRegistry : IReadOnlyCollection<IArtifactProvider>
{
    private readonly IReadOnlyList<IArtifactProvider> providers;
    private readonly IReadOnlyDictionary<string, IArtifactProvider> providersById;

    /// <summary>
    /// Empty artifact provider registry.
    /// </summary>
    public static ArtifactRegistry Empty { get; } = new(Array.Empty<IArtifactProvider>());

    /// <summary>
    /// Creates an empty artifact provider registry.
    /// </summary>
    public ArtifactRegistry()
        : this(Array.Empty<IArtifactProvider>())
    {
    }

    /// <summary>
    /// Creates an artifact provider registry from the supplied providers.
    /// </summary>
    public ArtifactRegistry(IEnumerable<IArtifactProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var orderedProviders = new List<IArtifactProvider>();
        var indexedProviders = new Dictionary<string, IArtifactProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);

            var providerId = provider.Descriptor.Id;
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new InvalidOperationException("Artifact provider ids must be non-empty.");
            }

            if (indexedProviders.ContainsKey(providerId))
            {
                throw new InvalidOperationException($"An artifact provider with id '{providerId}' has already been registered.");
            }

            orderedProviders.Add(provider);
            indexedProviders.Add(providerId, provider);
        }

        this.providers = orderedProviders;
        providersById = indexedProviders;
    }

    /// <summary>
    /// Number of registered artifact providers.
    /// </summary>
    public int Count => providers.Count;

    /// <summary>
    /// Returns a new registry that includes the supplied provider.
    /// </summary>
    public ArtifactRegistry Add(IArtifactProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return AddRange(new[] { provider });
    }

    /// <summary>
    /// Returns a new registry that includes the supplied providers.
    /// </summary>
    public ArtifactRegistry AddRange(IEnumerable<IArtifactProvider> additionalProviders)
    {
        ArgumentNullException.ThrowIfNull(additionalProviders);
        return new ArtifactRegistry(providers.Concat(additionalProviders));
    }

    /// <summary>
    /// Determines whether a provider with the supplied id is registered.
    /// </summary>
    public bool Contains(string providerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        return providersById.ContainsKey(providerId);
    }

    /// <summary>
    /// Attempts to resolve a provider by id.
    /// </summary>
    public bool TryGet(string providerId, out IArtifactProvider? provider)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        return providersById.TryGetValue(providerId, out provider);
    }

    /// <summary>
    /// Validates registered provider ids.
    /// </summary>
    public void Validate()
    {
        _ = new ArtifactRegistry(providers);
    }

    /// <summary>
    /// Returns an enumerator over registered providers.
    /// </summary>
    public IEnumerator<IArtifactProvider> GetEnumerator() => providers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
