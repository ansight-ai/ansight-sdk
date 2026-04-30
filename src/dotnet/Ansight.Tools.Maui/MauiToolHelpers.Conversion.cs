namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

internal static partial class MauiToolHelpers
{
    internal static JsonNode? CreateSimpleJsonValue(object value)
    {
        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (value is Color color)
        {
            return JsonValue.Create(color.ToArgbHex());
        }

        if (value is Thickness thickness)
        {
            return new JsonObject
            {
                ["left"] = thickness.Left,
                ["top"] = thickness.Top,
                ["right"] = thickness.Right,
                ["bottom"] = thickness.Bottom
            };
        }

        if (value is GridLength gridLength)
        {
            return JsonValue.Create(FormatGridLength(gridLength));
        }

        if (value is Point point)
        {
            return new JsonObject
            {
                ["x"] = point.X,
                ["y"] = point.Y
            };
        }

        if (value is Size size)
        {
            return new JsonObject
            {
                ["width"] = size.Width,
                ["height"] = size.Height
            };
        }

        if (value is Rect rect)
        {
            return new JsonObject
            {
                ["x"] = rect.X,
                ["y"] = rect.Y,
                ["width"] = rect.Width,
                ["height"] = rect.Height
            };
        }

        if (type.IsEnum)
        {
            return JsonValue.Create(value.ToString());
        }

        if (type == typeof(string))
        {
            return JsonValue.Create((string)value);
        }

        if (type == typeof(char) ||
            type == typeof(Guid) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan))
        {
            return JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        if (type == typeof(bool))
        {
            return JsonValue.Create((bool)value);
        }

        if (type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong))
        {
            return JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture));
        }

        return null;
    }

    internal static string FormatGridLength(GridLength gridLength)
    {
        if (gridLength.IsAuto)
        {
            return "auto";
        }

        if (!gridLength.IsStar)
        {
            return gridLength.Value.ToString("G", CultureInfo.InvariantCulture);
        }

        return Math.Abs(gridLength.Value - 1d) < double.Epsilon
            ? "*"
            : $"{gridLength.Value.ToString("G", CultureInfo.InvariantCulture)}*";
    }

    internal static object? ConvertJsonArgument(string rawValue, Type targetType)
    {
        var node = ParseJsonArgument(rawValue);
        return ConvertJsonValue(node, targetType);
    }

    internal static object? ConvertJsonArgumentToUntyped(string rawValue)
    {
        var node = ParseJsonArgument(rawValue);
        return ConvertJsonNodeToUntyped(node);
    }

    internal static object? ConvertJsonNodeToUntyped(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonValue jsonValue => ConvertJsonValueToUntyped(jsonValue),
            JsonArray jsonArray => jsonArray.Select(ConvertJsonNodeToUntyped).ToArray(),
            JsonObject jsonObject => jsonObject.ToDictionary(property => property.Key, property => ConvertJsonNodeToUntyped(property.Value), StringComparer.Ordinal),
            _ => node.ToJsonString()
        };
    }

    internal static object? ConvertJsonValueToUntyped(JsonValue jsonValue)
    {
        if (jsonValue.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return jsonValue.ToString();
    }

    internal static bool AreValuesEquivalent(object? actualValue, Type targetType, string expectedJson)
    {
        object? expectedValue;
        try
        {
            expectedValue = ConvertJsonArgument(expectedJson, targetType);
        }
        catch
        {
            expectedValue = ConvertJsonArgumentToUntyped(expectedJson);
        }

        if (actualValue == null || expectedValue == null)
        {
            return actualValue == null && expectedValue == null;
        }

        if (Equals(actualValue, expectedValue))
        {
            return true;
        }

        return string.Equals(
            Convert.ToString(actualValue, CultureInfo.InvariantCulture),
            Convert.ToString(expectedValue, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    internal static JsonNode? ParseJsonArgument(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return JsonValue.Create(string.Empty);
        }

        try
        {
            return JsonNode.Parse(rawValue);
        }
        catch (JsonException)
        {
            return JsonValue.Create(rawValue);
        }
    }

    internal static object? ConvertJsonValue(JsonNode? node, Type targetType)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var effectiveType = nullableType ?? targetType;

        if (node == null)
        {
            if (nullableType != null || !targetType.IsValueType)
            {
                return null;
            }

            throw new InvalidOperationException($"The target type '{GetTypeDisplayName(targetType)}' does not accept null.");
        }

        if (effectiveType == typeof(string))
        {
            return GetScalarString(node);
        }

        if (effectiveType == typeof(bool))
        {
            return GetBooleanValue(node);
        }

        if (effectiveType == typeof(char))
        {
            var text = GetScalarString(node);
            return text.Length == 1 ? text[0] : throw new InvalidOperationException("Character values must contain exactly one character.");
        }

        if (effectiveType.IsEnum)
        {
            return ConvertEnumValue(node, effectiveType);
        }

        if (IsNumericType(effectiveType))
        {
            return Convert.ChangeType(GetScalarValue(node), effectiveType, CultureInfo.InvariantCulture);
        }

        if (effectiveType == typeof(Guid))
        {
            return Guid.Parse(GetScalarString(node));
        }

        if (effectiveType == typeof(DateTime))
        {
            return DateTime.Parse(GetScalarString(node), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (effectiveType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(GetScalarString(node), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (effectiveType == typeof(TimeSpan))
        {
            return TimeSpan.Parse(GetScalarString(node), CultureInfo.InvariantCulture);
        }

        if (effectiveType == typeof(Color))
        {
            return ConvertColorValue(node);
        }

        if (effectiveType == typeof(Thickness))
        {
            return ConvertThicknessValue(node);
        }

        if (effectiveType == typeof(GridLength))
        {
            return ConvertGridLengthValue(node);
        }

        if (effectiveType == typeof(Point))
        {
            return ConvertPointValue(node);
        }

        if (effectiveType == typeof(Size))
        {
            return ConvertSizeValue(node);
        }

        if (effectiveType == typeof(Rect))
        {
            return ConvertRectValue(node);
        }

        try
        {
            return JsonSerializer.Deserialize(node.ToJsonString(), effectiveType, jsonSerializerOptions);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not convert value to '{GetTypeDisplayName(targetType)}': {exception.Message}", exception);
        }
    }

    internal static object ConvertEnumValue(JsonNode node, Type enumType)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var intValue))
        {
            return Enum.ToObject(enumType, intValue);
        }

        return Enum.Parse(enumType, GetScalarString(node), ignoreCase: true);
    }

    internal static object ConvertColorValue(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            var red = GetObjectDouble(jsonObject, "red", "r");
            var green = GetObjectDouble(jsonObject, "green", "g");
            var blue = GetObjectDouble(jsonObject, "blue", "b");
            var alpha = GetObjectDouble(jsonObject, "alpha", "a", defaultValue: 1d);
            return Color.FromRgba(red, green, blue, alpha);
        }

        return Color.FromArgb(GetScalarString(node));
    }

    internal static object ConvertThicknessValue(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            return new Thickness(
                GetObjectDouble(jsonObject, "left"),
                GetObjectDouble(jsonObject, "top"),
                GetObjectDouble(jsonObject, "right"),
                GetObjectDouble(jsonObject, "bottom"));
        }

        if (TryGetDouble(node, out var uniformValue))
        {
            return new Thickness(uniformValue);
        }

        var parts = SplitNumbers(GetScalarString(node));
        return parts.Length switch
        {
            1 => new Thickness(parts[0]),
            2 => new Thickness(parts[0], parts[1]),
            4 => new Thickness(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new InvalidOperationException("Thickness values must be a number, two numbers, four numbers, or an object with left/top/right/bottom.")
        };
    }

    internal static object ConvertGridLengthValue(JsonNode node)
    {
        if (TryGetDouble(node, out var absoluteValue))
        {
            return new GridLength(absoluteValue);
        }

        var text = GetScalarString(node).Trim();
        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridLength.Auto;
        }

        if (text.EndsWith("*", StringComparison.Ordinal))
        {
            var multiplierText = text[..^1].Trim();
            var multiplier = string.IsNullOrWhiteSpace(multiplierText)
                ? 1d
                : double.Parse(multiplierText, CultureInfo.InvariantCulture);
            return new GridLength(multiplier, GridUnitType.Star);
        }

        return new GridLength(double.Parse(text, CultureInfo.InvariantCulture));
    }

    internal static object ConvertPointValue(JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            var parts = SplitNumbers(GetScalarString(node));
            return parts.Length == 2
                ? new Point(parts[0], parts[1])
                : throw new InvalidOperationException("Point values must be two numbers or an object with x/y.");
        }

        return new Point(GetObjectDouble(jsonObject, "x"), GetObjectDouble(jsonObject, "y"));
    }

    internal static object ConvertSizeValue(JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            var parts = SplitNumbers(GetScalarString(node));
            return parts.Length == 2
                ? new Size(parts[0], parts[1])
                : throw new InvalidOperationException("Size values must be two numbers or an object with width/height.");
        }

        return new Size(GetObjectDouble(jsonObject, "width"), GetObjectDouble(jsonObject, "height"));
    }

    internal static object ConvertRectValue(JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            var parts = SplitNumbers(GetScalarString(node));
            return parts.Length == 4
                ? new Rect(parts[0], parts[1], parts[2], parts[3])
                : throw new InvalidOperationException("Rect values must be four numbers or an object with x/y/width/height.");
        }

        return new Rect(
            GetObjectDouble(jsonObject, "x"),
            GetObjectDouble(jsonObject, "y"),
            GetObjectDouble(jsonObject, "width"),
            GetObjectDouble(jsonObject, "height"));
    }

    internal static object GetScalarValue(JsonNode node)
    {
        if (node is not JsonValue jsonValue)
        {
            return node.ToJsonString();
        }

        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (jsonValue.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return jsonValue.ToString();
    }

    internal static string GetScalarString(JsonNode node)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return node.ToJsonString();
    }

    internal static bool GetBooleanValue(JsonNode node)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        return bool.Parse(GetScalarString(node));
    }

    internal static bool TryGetDouble(JsonNode node, out double value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<double>(out value))
        {
            return true;
        }

        return double.TryParse(GetScalarString(node), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    internal static double GetObjectDouble(JsonObject jsonObject, string name, string? alternateName = null, double? defaultValue = null)
    {
        var node = jsonObject[name] ?? (alternateName == null ? null : jsonObject[alternateName]);
        if (node != null && TryGetDouble(node, out var value))
        {
            return value;
        }

        if (defaultValue.HasValue)
        {
            return defaultValue.Value;
        }

        throw new InvalidOperationException($"The numeric property '{name}' is required.");
    }

    internal static double[] SplitNumbers(string value)
    {
        return value
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
    }

    internal static bool IsTypeNameMatch(Type type, string typeName)
        => string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase);
}
#endif
