namespace Ansight.Tools.Preferences;

public sealed class PreferencesToolsOptions
{
    internal static PreferencesToolsOptions Default { get; } = new(
        defaultStore: null,
        allowedStores: Array.Empty<string>(),
        allowedKeys: Array.Empty<string>(),
        allowedKeyPrefixes: Array.Empty<string>());

    internal PreferencesToolsOptions(
        string? defaultStore,
        IReadOnlyCollection<string> allowedStores,
        IReadOnlyCollection<string> allowedKeys,
        IReadOnlyCollection<string> allowedKeyPrefixes)
    {
        DefaultStore = string.IsNullOrWhiteSpace(defaultStore) ? null : defaultStore.Trim();
        AllowedStores = allowedStores;
        AllowedKeys = allowedKeys;
        AllowedKeyPrefixes = allowedKeyPrefixes;
    }

    public string? DefaultStore { get; }

    public IReadOnlyCollection<string> AllowedStores { get; }

    public IReadOnlyCollection<string> AllowedKeys { get; }

    public IReadOnlyCollection<string> AllowedKeyPrefixes { get; }

    public static PreferencesToolsOptionsBuilder CreateBuilder() => new();
}

public sealed class PreferencesToolsOptionsBuilder
{
    private readonly HashSet<string> allowedStores = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> allowedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> allowedKeyPrefixes = new(StringComparer.Ordinal);
    private string? defaultStore;

    public PreferencesToolsOptionsBuilder WithDefaultStore(string? store)
    {
        defaultStore = string.IsNullOrWhiteSpace(store) ? null : store.Trim();
        return this;
    }

    public PreferencesToolsOptionsBuilder AllowStore(string store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(store);

        allowedStores.Add(store.Trim());
        return this;
    }

    public PreferencesToolsOptionsBuilder AllowStores(params string[] stores)
    {
        ArgumentNullException.ThrowIfNull(stores);

        foreach (var store in stores)
        {
            AllowStore(store);
        }

        return this;
    }

    public PreferencesToolsOptionsBuilder AllowKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        allowedKeys.Add(key.Trim());
        return this;
    }

    public PreferencesToolsOptionsBuilder AllowKeys(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            AllowKey(key);
        }

        return this;
    }

    public PreferencesToolsOptionsBuilder AllowKeyPrefix(string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        allowedKeyPrefixes.Add(keyPrefix.Trim());
        return this;
    }

    public PreferencesToolsOptionsBuilder AllowKeyPrefixes(params string[] keyPrefixes)
    {
        ArgumentNullException.ThrowIfNull(keyPrefixes);

        foreach (var keyPrefix in keyPrefixes)
        {
            AllowKeyPrefix(keyPrefix);
        }

        return this;
    }

    public PreferencesToolsOptions Build()
        => new(
            defaultStore,
            allowedStores.ToArray(),
            allowedKeys.ToArray(),
            allowedKeyPrefixes.ToArray());
}
