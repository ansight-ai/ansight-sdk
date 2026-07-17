namespace Ansight.Tools.VisualTree;

/// <summary>
/// Process-wide registry of visual-tree providers available to local capture and remote tools.
/// </summary>
public static class VisualTreeProviderRegistry
{
    /// <summary>
    /// Source identifier for the built-in platform visual tree.
    /// </summary>
    public const string NativeSource = "native";

    private static readonly Lock gate = new();
    private static readonly Dictionary<string, IVisualTreeProvider> providers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [NativeSource] = NativeVisualTreeProvider.Instance
        };

    /// <summary>
    /// Registers a provider and returns a handle that removes that exact registration.
    /// </summary>
    public static IDisposable Register(IVisualTreeProvider provider, bool replaceExisting = true)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var source = NormalizeSource(provider.Source);

        lock (gate)
        {
            if (!replaceExisting && providers.ContainsKey(source))
            {
                throw new InvalidOperationException($"A visual tree provider for source '{source}' is already registered.");
            }

            providers[source] = provider;
        }

        return new Registration(source, provider);
    }

    /// <summary>
    /// Resolves a provider. A null or blank source resolves the built-in native provider.
    /// </summary>
    public static bool TryGet(string? source, out IVisualTreeProvider? provider)
    {
        var normalizedSource = NormalizeSourceOrDefault(source);
        lock (gate)
        {
            return providers.TryGetValue(normalizedSource, out provider);
        }
    }

    /// <summary>
    /// Returns a stable snapshot of all registered providers ordered by source.
    /// </summary>
    public static IReadOnlyList<IVisualTreeProvider> GetProviders()
    {
        lock (gate)
        {
            return providers
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.Value)
                .ToArray();
        }
    }

    /// <summary>
    /// Returns the normalized sources of all registered providers in ordinal order.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredSources()
    {
        lock (gate)
        {
            return providers.Keys.OrderBy(source => source, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>
    /// Returns descriptors for every registered provider ordered by source.
    /// </summary>
    public static IReadOnlyList<VisualTreeProviderDescriptor> Query()
        => GetProviders()
            .Select(provider => new VisualTreeProviderDescriptor(
                NormalizeSource(provider.Source),
                provider.DisplayName))
            .ToArray();

    internal static string NormalizeSourceOrDefault(string? source)
    {
        var normalized = source?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? NativeSource : normalized;
    }

    internal static string NormalizeSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var normalized = source.Trim().ToLowerInvariant();
        if (normalized.Length > 64)
        {
            throw new ArgumentException("Visual tree provider source must be at most 64 characters.", nameof(source));
        }

        return normalized;
    }

    private static void Unregister(string source, IVisualTreeProvider provider)
    {
        lock (gate)
        {
            if (providers.TryGetValue(source, out var current) && ReferenceEquals(current, provider))
            {
                providers.Remove(source);
            }
        }
    }

    private sealed class Registration(string source, IVisualTreeProvider provider) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Unregister(source, provider);
        }
    }
}
