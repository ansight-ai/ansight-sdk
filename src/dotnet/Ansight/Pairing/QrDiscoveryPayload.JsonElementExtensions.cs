using System.Globalization;
using System.Text.Json;

namespace Ansight.Pairing;

internal static class QrDiscoveryPayloadJsonElementExtensions
{
    public static bool HasAnyProperty(this JsonElement element, IReadOnlyList<string> propertyNames)
    {
        return element.TryGetPropertyValue(propertyNames, out _);
    }

    public static bool TryGetObjectPropertyValue(
        this JsonElement element,
        IReadOnlyList<string> propertyNames,
        out JsonElement value)
    {
        value = default;
        return element.TryGetPropertyValue(propertyNames, out value) &&
               value.ValueKind == JsonValueKind.Object;
    }

    public static bool TryGetPropertyValue(
        this JsonElement element,
        IReadOnlyList<string> propertyNames,
        out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            foreach (var propertyName in propertyNames)
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryReadRequiredStringValue(
        this JsonElement element,
        IReadOnlyList<string> propertyNames,
        out string value)
    {
        value = string.Empty;
        if (!element.TryReadOptionalStringValue(propertyNames, out var optionalValue) ||
            string.IsNullOrWhiteSpace(optionalValue))
        {
            return false;
        }

        value = optionalValue;
        return true;
    }

    public static bool TryReadOptionalStringValue(
        this JsonElement element,
        IReadOnlyList<string> propertyNames,
        out string? value)
    {
        value = null;
        if (!element.TryGetPropertyValue(propertyNames, out var propertyValue))
        {
            return true;
        }

        switch (propertyValue.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.String:
                value = propertyValue.GetString();
                return true;

            default:
                return false;
        }
    }

    public static bool TryReadRequiredDateTimeOffsetValue(
        this JsonElement element,
        IReadOnlyList<string> propertyNames,
        out DateTimeOffset value)
    {
        value = default;
        if (!element.TryReadOptionalDateTimeOffsetValue(propertyNames, out var optionalValue) ||
            optionalValue is null)
        {
            return false;
        }

        value = optionalValue.Value;
        return true;
    }

    public static bool TryReadOptionalDateTimeOffsetValue(
        this JsonElement element,
        IReadOnlyList<string> propertyNames,
        out DateTimeOffset? value)
    {
        value = null;
        if (!element.TryGetPropertyValue(propertyNames, out var propertyValue))
        {
            return true;
        }

        switch (propertyValue.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.Number:
                if (propertyValue.TryGetInt64(out var unixTimeSeconds))
                {
                    value = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
                    return true;
                }

                return false;

            case JsonValueKind.String:
                var raw = propertyValue.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return true;
                }

                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out unixTimeSeconds))
                {
                    value = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
                    return true;
                }

                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedValue))
                {
                    value = parsedValue;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    public static bool TryReadRequiredBooleanValue(
        this JsonElement element,
        IReadOnlyList<string> propertyNames,
        out bool value)
    {
        value = false;
        if (!element.TryGetPropertyValue(propertyNames, out var propertyValue))
        {
            return false;
        }

        switch (propertyValue.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;

            case JsonValueKind.False:
                value = false;
                return true;

            case JsonValueKind.Number:
                if (propertyValue.TryGetInt32(out var numericFlag))
                {
                    if (numericFlag == 1)
                    {
                        value = true;
                        return true;
                    }

                    if (numericFlag == 0)
                    {
                        value = false;
                        return true;
                    }
                }

                return false;

            case JsonValueKind.String:
                var raw = propertyValue.GetString();
                if (string.Equals(raw, "1", StringComparison.Ordinal) ||
                    bool.TryParse(raw, out value))
                {
                    return true;
                }

                if (string.Equals(raw, "0", StringComparison.Ordinal))
                {
                    value = false;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }
}
