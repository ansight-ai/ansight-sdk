namespace Ansight.Tools.SecureStorage;

using System.Text.Json.Nodes;

internal sealed record SecureStorageValueResult(string Store, string Key, bool Exists, string? Value);

internal sealed record SecureStorageWriteResult(string Store, string Key, bool Updated);

internal sealed record SecureStorageRemoveResult(string Store, string Key, bool Removed);

internal interface ISecureStorageBackend
{
    SecureStorageValueResult GetValue(string key);

    SecureStorageWriteResult SetValue(string key, string value);

    SecureStorageRemoveResult RemoveKey(string key);
}

internal static class SecureStorageSupport
{
    internal static Func<SecureStorageToolsOptions, ISecureStorageBackend>? BackendFactoryOverride { get; set; }

    internal static JsonObject GetValue(SecureStorageToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var key = GetRequiredString(arguments, "key");
        EnsureKeyAllowed(options, key);

        var result = GetBackend(options).GetValue(key);

        return new JsonObject
        {
            ["store"] = result.Store,
            ["key"] = result.Key,
            ["exists"] = result.Exists,
            ["value"] = result.Value,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject SetValue(SecureStorageToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var key = GetRequiredString(arguments, "key");
        EnsureKeyAllowed(options, key);

        var value = GetRequiredString(arguments, "value");
        var result = GetBackend(options).SetValue(key, value);

        return new JsonObject
        {
            ["store"] = result.Store,
            ["key"] = result.Key,
            ["updated"] = result.Updated,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject RemoveKey(SecureStorageToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var key = GetRequiredString(arguments, "key");
        EnsureKeyAllowed(options, key);

        var result = GetBackend(options).RemoveKey(key);

        return new JsonObject
        {
            ["store"] = result.Store,
            ["key"] = result.Key,
            ["removed"] = result.Removed,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static string GetRequiredString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The argument '{key}' is required.");
        }

        return value.Trim();
    }

    internal static string ResolveAndroidStore(SecureStorageToolsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AndroidStore))
        {
            return options.AndroidStore!;
        }

#if ANDROID
        return Android.App.Application.Context?.PackageName + ".ansight.secure_storage";
#else
        return "ansight.secure_storage";
#endif
    }

    internal static string ResolveAppleService(SecureStorageToolsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AppleService))
        {
            return options.AppleService!;
        }

#if IOS || MACCATALYST
        return Foundation.NSBundle.MainBundle.BundleIdentifier ?? "ansight.secure_storage";
#else
        return "ansight.secure_storage";
#endif
    }

    private static ISecureStorageBackend GetBackend(SecureStorageToolsOptions options)
        => BackendFactoryOverride?.Invoke(options) ?? CreateBackend(options);

    private static void EnsureKeyAllowed(SecureStorageToolsOptions options, string key)
    {
        if (IsKeyAllowed(options, key))
        {
            return;
        }

        throw new InvalidOperationException($"The secure storage key '{key}' is not allowed by the current registration.");
    }

    private static bool IsKeyAllowed(SecureStorageToolsOptions options, string key)
    {
        if (options.AllowedKeys.Count == 0 && options.AllowedKeyPrefixes.Count == 0)
        {
            return false;
        }

        if (options.AllowedKeys.Contains(key, StringComparer.Ordinal))
        {
            return true;
        }

        return options.AllowedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static ISecureStorageBackend CreateBackend(SecureStorageToolsOptions options)
    {
#if ANDROID
        return new AndroidSecureStorageBackend(options);
#elif IOS || MACCATALYST
        return new AppleSecureStorageBackend(options);
#else
        return new UnsupportedSecureStorageBackend();
#endif
    }
}
