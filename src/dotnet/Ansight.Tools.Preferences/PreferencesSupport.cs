namespace Ansight.Tools.Preferences;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

internal enum PreferenceValueKind
{
    String = 0,
    Boolean = 1,
    Integer = 2,
    Number = 3,
    StringArray = 4,
    Unsupported = 5
}

internal sealed record PreferenceListKeysResult(string Store, IReadOnlyList<string> Keys);

internal sealed record PreferenceValueResult(string Store, string Key, bool Exists, string? Value, PreferenceValueKind? ValueKind);

internal sealed record PreferenceWriteResult(string Store, string Key, PreferenceValueKind ValueKind, bool Updated);

internal sealed record PreferenceRemoveResult(string Store, string Key, bool Removed);

internal interface IPreferencesBackend
{
    PreferenceListKeysResult ListKeys(string? store);

    PreferenceValueResult GetValue(string? store, string key);

    PreferenceWriteResult SetValue(string? store, string key, PreferenceValueKind valueKind, string value);

    PreferenceRemoveResult RemoveKey(string? store, string key);
}

internal static class PreferencesSupport
{
    internal static Func<IPreferencesBackend>? BackendFactoryOverride { get; set; }

    internal static JsonObject ListKeys(PreferencesToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var requestedStore = GetString(arguments, "store") ?? options.DefaultStore;
        var requestedPrefix = GetString(arguments, "prefix");
        var maxResults = GetInt(arguments, "maxResults", defaultValue: 200, minimum: 1, maximum: 1000);
        var backend = GetBackend();

        var result = backend.ListKeys(requestedStore);
        EnsureStoreAllowed(options, result.Store);

        var matchingKeys = result.Keys
            .Where(key => IsKeyAllowed(options, key))
            .Where(key => string.IsNullOrWhiteSpace(requestedPrefix) || key.StartsWith(requestedPrefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var truncated = matchingKeys.Count > maxResults;
        var keys = new JsonArray();
        foreach (var key in matchingKeys.Take(maxResults))
        {
            keys.Add(key);
        }

        return new JsonObject
        {
            ["store"] = result.Store,
            ["keys"] = keys,
            ["truncated"] = truncated,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject GetValue(PreferencesToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var key = GetRequiredString(arguments, "key");
        EnsureKeyAllowed(options, key);

        var requestedStore = GetString(arguments, "store") ?? options.DefaultStore;
        var backend = GetBackend();
        var result = backend.GetValue(requestedStore, key);

        EnsureStoreAllowed(options, result.Store);

        return new JsonObject
        {
            ["store"] = result.Store,
            ["key"] = result.Key,
            ["exists"] = result.Exists,
            ["value"] = result.Value,
            ["valueType"] = result.ValueKind is null ? null : ToValueTypeString(result.ValueKind.Value),
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject SetValue(PreferencesToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var key = GetRequiredString(arguments, "key");
        EnsureKeyAllowed(options, key);

        var value = GetRequiredString(arguments, "value");
        var valueKind = ParseValueKind(GetRequiredString(arguments, "valueType"));
        var requestedStore = GetString(arguments, "store") ?? options.DefaultStore;
        var backend = GetBackend();
        var result = backend.SetValue(requestedStore, key, valueKind, value);

        EnsureStoreAllowed(options, result.Store);

        return new JsonObject
        {
            ["store"] = result.Store,
            ["key"] = result.Key,
            ["valueType"] = ToValueTypeString(result.ValueKind),
            ["updated"] = result.Updated,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static JsonObject RemoveKey(PreferencesToolsOptions options, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var key = GetRequiredString(arguments, "key");
        EnsureKeyAllowed(options, key);

        var requestedStore = GetString(arguments, "store") ?? options.DefaultStore;
        var backend = GetBackend();
        var result = backend.RemoveKey(requestedStore, key);

        EnsureStoreAllowed(options, result.Store);

        return new JsonObject
        {
            ["store"] = result.Store,
            ["key"] = result.Key,
            ["removed"] = result.Removed,
            ["capturedAtUtc"] = DateTime.UtcNow.ToString("O")
        };
    }

    internal static int GetInt(IReadOnlyDictionary<string, string> arguments, string key, int defaultValue, int minimum, int maximum)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return Math.Clamp(parsedValue, minimum, maximum);
    }

    internal static string? GetString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    internal static string GetRequiredString(IReadOnlyDictionary<string, string> arguments, string key)
        => GetString(arguments, key) ?? throw new InvalidOperationException($"The argument '{key}' is required.");

    internal static string NormalizeStringValue(string? value)
        => value ?? string.Empty;

    internal static string NormalizeBooleanValue(bool value)
        => value ? "true" : "false";

    internal static string NormalizeIntegerValue(long value)
        => value.ToString(CultureInfo.InvariantCulture);

    internal static string NormalizeNumberValue(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    internal static string NormalizeStringArrayValue(IEnumerable<string> values)
        => JsonSerializer.Serialize(values);

    internal static string[] ParseStringArray(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            var values = JsonSerializer.Deserialize<string[]>(value);
            return values ?? Array.Empty<string>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The 'string_array' value must be a JSON string array.", exception);
        }
    }

    internal static PreferenceValueKind ParseValueKind(string valueType)
    {
        return valueType.Trim().ToLowerInvariant() switch
        {
            "string" => PreferenceValueKind.String,
            "boolean" => PreferenceValueKind.Boolean,
            "integer" => PreferenceValueKind.Integer,
            "number" => PreferenceValueKind.Number,
            "string_array" => PreferenceValueKind.StringArray,
            _ => throw new InvalidOperationException($"The value type '{valueType}' is not supported.")
        };
    }

    internal static string ToValueTypeString(PreferenceValueKind valueKind) => valueKind switch
    {
        PreferenceValueKind.String => "string",
        PreferenceValueKind.Boolean => "boolean",
        PreferenceValueKind.Integer => "integer",
        PreferenceValueKind.Number => "number",
        PreferenceValueKind.StringArray => "string_array",
        PreferenceValueKind.Unsupported => "unsupported",
        _ => throw new ArgumentOutOfRangeException(nameof(valueKind), valueKind, null)
    };

    private static IPreferencesBackend GetBackend()
        => BackendFactoryOverride?.Invoke() ?? CreateBackend();

    private static void EnsureStoreAllowed(PreferencesToolsOptions options, string store)
    {
        if (options.AllowedStores.Count == 0)
        {
            return;
        }

        if (options.AllowedStores.Contains(store, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"The preferences store '{store}' is not allowed by the current registration.");
    }

    private static void EnsureKeyAllowed(PreferencesToolsOptions options, string key)
    {
        if (IsKeyAllowed(options, key))
        {
            return;
        }

        throw new InvalidOperationException($"The preferences key '{key}' is not allowed by the current registration.");
    }

    private static bool IsKeyAllowed(PreferencesToolsOptions options, string key)
    {
        if (options.AllowedKeys.Count == 0 && options.AllowedKeyPrefixes.Count == 0)
        {
            return true;
        }

        if (options.AllowedKeys.Contains(key, StringComparer.Ordinal))
        {
            return true;
        }

        return options.AllowedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static IPreferencesBackend CreateBackend()
    {
#if ANDROID
        return new AndroidPreferencesBackend();
#elif IOS || MACCATALYST
        return new ApplePreferencesBackend();
#else
        return new UnsupportedPreferencesBackend();
#endif
    }
}
