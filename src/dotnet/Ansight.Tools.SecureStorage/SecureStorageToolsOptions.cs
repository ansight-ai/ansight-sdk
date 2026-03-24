namespace Ansight.Tools.SecureStorage;

public sealed class SecureStorageToolsOptions
{
    internal static SecureStorageToolsOptions Default { get; } = new(
        androidStore: null,
        appleService: null,
        allowedKeys: Array.Empty<string>(),
        allowedKeyPrefixes: Array.Empty<string>());

    internal SecureStorageToolsOptions(
        string? androidStore,
        string? appleService,
        IReadOnlyCollection<string> allowedKeys,
        IReadOnlyCollection<string> allowedKeyPrefixes)
    {
        AndroidStore = string.IsNullOrWhiteSpace(androidStore) ? null : androidStore.Trim();
        AppleService = string.IsNullOrWhiteSpace(appleService) ? null : appleService.Trim();
        AllowedKeys = allowedKeys;
        AllowedKeyPrefixes = allowedKeyPrefixes;
    }

    public string? AndroidStore { get; }

    public string? AppleService { get; }

    public IReadOnlyCollection<string> AllowedKeys { get; }

    public IReadOnlyCollection<string> AllowedKeyPrefixes { get; }

    public static SecureStorageToolsOptionsBuilder CreateBuilder() => new();
}

public sealed class SecureStorageToolsOptionsBuilder
{
    private readonly HashSet<string> allowedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> allowedKeyPrefixes = new(StringComparer.Ordinal);
    private string? storageIdentifier;
    private string? androidStore;
    private string? appleService;

    public SecureStorageToolsOptionsBuilder WithStorageIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        storageIdentifier = identifier.Trim();
        return this;
    }

    public SecureStorageToolsOptionsBuilder WithAndroidStore(string store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(store);

        androidStore = store.Trim();
        return this;
    }

    public SecureStorageToolsOptionsBuilder WithAppleService(string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        appleService = service.Trim();
        return this;
    }

    public SecureStorageToolsOptionsBuilder AllowKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        allowedKeys.Add(key.Trim());
        return this;
    }

    public SecureStorageToolsOptionsBuilder AllowKeys(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            AllowKey(key);
        }

        return this;
    }

    public SecureStorageToolsOptionsBuilder AllowKeyPrefix(string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        allowedKeyPrefixes.Add(keyPrefix.Trim());
        return this;
    }

    public SecureStorageToolsOptionsBuilder AllowKeyPrefixes(params string[] keyPrefixes)
    {
        ArgumentNullException.ThrowIfNull(keyPrefixes);

        foreach (var keyPrefix in keyPrefixes)
        {
            AllowKeyPrefix(keyPrefix);
        }

        return this;
    }

    public SecureStorageToolsOptions Build()
    {
        var resolvedAndroidStore = androidStore ?? storageIdentifier;
        var resolvedAppleService = appleService ?? storageIdentifier;

        return new SecureStorageToolsOptions(
            resolvedAndroidStore,
            resolvedAppleService,
            allowedKeys.ToArray(),
            allowedKeyPrefixes.ToArray());
    }
}
