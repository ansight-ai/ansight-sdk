namespace Ansight.Tools.Preferences;

#if ANDROID
using Android.App;
using Android.Content;
using Java.Util;

internal sealed class AndroidPreferencesBackend : IPreferencesBackend
{
    public PreferenceListKeysResult ListKeys(string? store)
    {
        var (sharedPreferences, resolvedStore) = GetSharedPreferences(store);
        var keys = sharedPreferences.All?.Keys?.ToList() ?? new List<string>();
        return new PreferenceListKeysResult(resolvedStore, keys);
    }

    public PreferenceValueResult GetValue(string? store, string key)
    {
        var (sharedPreferences, resolvedStore) = GetSharedPreferences(store);
        if (!sharedPreferences.Contains(key))
        {
            return new PreferenceValueResult(resolvedStore, key, false, null, null);
        }

        var value = sharedPreferences.All?[key];
        return CreateValueResult(resolvedStore, key, value);
    }

    public PreferenceWriteResult SetValue(string? store, string key, PreferenceValueKind valueKind, string value)
    {
        var (sharedPreferences, resolvedStore) = GetSharedPreferences(store);
        using var editor = sharedPreferences.Edit()
            ?? throw new InvalidOperationException($"Unable to edit the shared preferences store '{resolvedStore}'.");

        switch (valueKind)
        {
            case PreferenceValueKind.String:
                editor.PutString(key, value);
                break;
            case PreferenceValueKind.Boolean:
                editor.PutBoolean(key, ParseBoolean(value));
                break;
            case PreferenceValueKind.Integer:
                var integerValue = ParseInteger(value);
                if (integerValue >= int.MinValue && integerValue <= int.MaxValue)
                {
                    editor.PutInt(key, (int)integerValue);
                }
                else
                {
                    editor.PutLong(key, integerValue);
                }

                break;
            case PreferenceValueKind.Number:
                var numberValue = ParseNumber(value);
                var floatValue = (float)numberValue;
                if (float.IsInfinity(floatValue) || float.IsNaN(floatValue))
                {
                    throw new InvalidOperationException("The provided numeric value is outside the supported Android SharedPreferences range.");
                }

                editor.PutFloat(key, floatValue);
                break;
            case PreferenceValueKind.StringArray:
                editor.PutStringSet(key, new HashSet<string>(PreferencesSupport.ParseStringArray(value)));
                break;
            default:
                throw new InvalidOperationException($"The value type '{PreferencesSupport.ToValueTypeString(valueKind)}' is not supported for writing.");
        }

        editor.Apply();
        return new PreferenceWriteResult(resolvedStore, key, valueKind, true);
    }

    public PreferenceRemoveResult RemoveKey(string? store, string key)
    {
        var (sharedPreferences, resolvedStore) = GetSharedPreferences(store);
        var removed = sharedPreferences.Contains(key);
        if (removed)
        {
            using var editor = sharedPreferences.Edit()
                ?? throw new InvalidOperationException($"Unable to edit the shared preferences store '{resolvedStore}'.");
            editor.Remove(key);
            editor.Apply();
        }

        return new PreferenceRemoveResult(resolvedStore, key, removed);
    }

    private static (ISharedPreferences SharedPreferences, string ResolvedStore) GetSharedPreferences(string? store)
    {
        var context = Application.Context ?? throw new InvalidOperationException("Android application context is not available.");
        var trimmedStore = string.IsNullOrWhiteSpace(store) ? null : store.Trim();

        if (string.IsNullOrWhiteSpace(trimmedStore) ||
            string.Equals(trimmedStore, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedStore, "standard", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedStore = context.PackageName + "_preferences";
            var sharedPreferences = context.GetSharedPreferences(resolvedStore, FileCreationMode.Private)
                ?? throw new InvalidOperationException($"Unable to access the shared preferences store '{resolvedStore}'.");
            return (sharedPreferences, resolvedStore);
        }

        var explicitPreferences = context.GetSharedPreferences(trimmedStore, FileCreationMode.Private)
            ?? throw new InvalidOperationException($"Unable to access the shared preferences store '{trimmedStore}'.");
        return (explicitPreferences, trimmedStore);
    }

    private static PreferenceValueResult CreateValueResult(string store, string key, object? value)
    {
        return value switch
        {
            null => new PreferenceValueResult(store, key, true, null, PreferenceValueKind.Unsupported),
            string stringValue => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeStringValue(stringValue), PreferenceValueKind.String),
            bool boolValue => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeBooleanValue(boolValue), PreferenceValueKind.Boolean),
            int intValue => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeIntegerValue(intValue), PreferenceValueKind.Integer),
            long longValue => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeIntegerValue(longValue), PreferenceValueKind.Integer),
            float floatValue => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeNumberValue(floatValue), PreferenceValueKind.Number),
            double doubleValue => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeNumberValue(doubleValue), PreferenceValueKind.Number),
            Java.Lang.String javaString => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeStringValue(javaString.ToString()), PreferenceValueKind.String),
            Java.Lang.Boolean javaBoolean => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeBooleanValue(javaBoolean.BooleanValue()), PreferenceValueKind.Boolean),
            Java.Lang.Integer javaInteger => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeIntegerValue(javaInteger.LongValue()), PreferenceValueKind.Integer),
            Java.Lang.Long javaLong => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeIntegerValue(javaLong.LongValue()), PreferenceValueKind.Integer),
            Java.Lang.Float javaFloat => new PreferenceValueResult(store, key, true, PreferencesSupport.NormalizeNumberValue(javaFloat.DoubleValue()), PreferenceValueKind.Number),
            ICollection collection => CreateStringArrayResult(store, key, collection),
            _ => new PreferenceValueResult(store, key, true, value.ToString(), PreferenceValueKind.Unsupported)
        };
    }

    private static PreferenceValueResult CreateStringArrayResult(string store, string key, ICollection values)
    {
        var items = new List<string>();
        var iterator = values.Iterator();
        while (iterator.HasNext)
        {
            items.Add(iterator.Next()?.ToString() ?? string.Empty);
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
