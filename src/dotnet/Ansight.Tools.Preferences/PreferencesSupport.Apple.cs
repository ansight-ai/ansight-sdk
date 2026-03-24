namespace Ansight.Tools.Preferences;

#if IOS || MACCATALYST
using Foundation;

internal sealed class ApplePreferencesBackend : IPreferencesBackend
{
    public PreferenceListKeysResult ListKeys(string? store)
    {
        var (defaults, resolvedStore) = GetDefaults(store);
        var dictionary = defaults.ToDictionary();
        var keys = dictionary.Keys
            .Select(key => key.ToString())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToList();

        return new PreferenceListKeysResult(resolvedStore, keys);
    }

    public PreferenceValueResult GetValue(string? store, string key)
    {
        var (defaults, resolvedStore) = GetDefaults(store);
        var value = defaults.ValueForKey(new NSString(key));
        if (value is null)
        {
            return new PreferenceValueResult(resolvedStore, key, false, null, null);
        }

        return CreateValueResult(resolvedStore, key, value);
    }

    public PreferenceWriteResult SetValue(string? store, string key, PreferenceValueKind valueKind, string value)
    {
        var (defaults, resolvedStore) = GetDefaults(store);

        switch (valueKind)
        {
            case PreferenceValueKind.String:
                defaults.SetString(value, key);
                break;
            case PreferenceValueKind.Boolean:
                defaults.SetBool(ParseBoolean(value), key);
                break;
            case PreferenceValueKind.Integer:
                var integerValue = ParseInteger(value);
                if (integerValue >= int.MinValue && integerValue <= int.MaxValue)
                {
                    defaults.SetInt((int)integerValue, key);
                }
                else
                {
                    defaults.SetValueForKey(NSNumber.FromLong((nint)integerValue), new NSString(key));
                }
                break;
            case PreferenceValueKind.Number:
                defaults.SetDouble(ParseNumber(value), key);
                break;
            case PreferenceValueKind.StringArray:
                var items = PreferencesSupport.ParseStringArray(value).Select(item => new NSString(item)).ToArray();
                defaults.SetValueForKey(NSArray<NSString>.FromNSObjects(items), new NSString(key));
                break;
            default:
                throw new InvalidOperationException($"The value type '{PreferencesSupport.ToValueTypeString(valueKind)}' is not supported for writing.");
        }

        _ = defaults.Synchronize();
        return new PreferenceWriteResult(resolvedStore, key, valueKind, true);
    }

    public PreferenceRemoveResult RemoveKey(string? store, string key)
    {
        var (defaults, resolvedStore) = GetDefaults(store);
        var removed = defaults.ValueForKey(new NSString(key)) is not null;
        if (removed)
        {
            defaults.RemoveObject(key);
            _ = defaults.Synchronize();
        }

        return new PreferenceRemoveResult(resolvedStore, key, removed);
    }

    private static (NSUserDefaults Defaults, string ResolvedStore) GetDefaults(string? store)
    {
        var trimmedStore = string.IsNullOrWhiteSpace(store) ? null : store.Trim();
        if (string.IsNullOrWhiteSpace(trimmedStore) ||
            string.Equals(trimmedStore, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedStore, "standard", StringComparison.OrdinalIgnoreCase))
        {
            return (NSUserDefaults.StandardUserDefaults, "standard");
        }

        var suiteDefaults = new NSUserDefaults(trimmedStore, NSUserDefaultsType.SuiteName);
        return (suiteDefaults, trimmedStore);
    }

    private static PreferenceValueResult CreateValueResult(string store, string key, NSObject value)
    {
        switch (value)
        {
            case NSString stringValue:
                return new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeStringValue(stringValue.ToString()), PreferenceValueKind.String);
            case NSNumber numberValue:
                return CreateNumberResult(store, key, numberValue);
            case NSArray arrayValue:
                return CreateArrayResult(store, key, arrayValue);
            default:
                return new PreferenceValueResult(store, key, true, value.Description, PreferenceValueKind.Unsupported);
        }
    }

    private static PreferenceValueResult CreateNumberResult(string store, string key, NSNumber value)
    {
        var objectiveCType = value.ObjCType?.ToString() ?? string.Empty;
        return objectiveCType switch
        {
            "c" or "B" => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeBooleanValue(value.BoolValue), PreferenceValueKind.Boolean),
            "f" or "d" => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeNumberValue(value.DoubleValue), PreferenceValueKind.Number),
            _ => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeIntegerValue(value.Int64Value), PreferenceValueKind.Integer)
        };
    }

    private static PreferenceValueResult CreateArrayResult(string store, string key, NSArray value)
    {
        var items = new List<string>();
        for (nuint i = 0; i < value.Count; i++)
        {
            if (value.GetItem<NSObject>(i) is not NSString stringValue)
            {
                return new PreferenceValueResult(store, key, true, value.Description, PreferenceValueKind.Unsupported);
            }

            items.Add(stringValue.ToString());
        }

        return new PreferenceValueResult(
            store,
            key,
            true,
            PreferencesSupport.NormalizeStringArrayValue(items),
            PreferenceValueKind.StringArray);
    }

    private static bool ParseBoolean(string value)
    {
        if (bool.TryParse(value, out var parsedValue))
        {
            return parsedValue;
        }

        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException("The provided boolean value is invalid.")
        };
    }

    private static long ParseInteger(string value)
    {
        if (long.TryParse(value, out var parsedValue))
        {
            return parsedValue;
        }

        throw new InvalidOperationException("The provided integer value is invalid.");
    }

    private static double ParseNumber(string value)
    {
        if (double.TryParse(value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        throw new InvalidOperationException("The provided numeric value is invalid.");
    }
}
#endif
