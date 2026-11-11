using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ansight.Tools.Avalonia;

internal static class AvaloniaValueFormatter
{
    private static readonly string[] sensitiveWords =
    [
        "token", "password", "passcode", "secret", "credential", "authorization", "bearer",
        "api key", "apikey", "private key", "account number", "card number", "credit card",
        "routing number", "social security", "ssn", "cvv", "cvc", "otp", "one time", "mfa"
    ];

    internal static string? Label(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (sensitiveWords.Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return "[redacted]";
        }

        var atIndex = value.IndexOf('@');
        var looksLikeEmail = atIndex > 0
            && atIndex < value.Length - 3
            && value.IndexOf('.', atIndex + 2) > atIndex + 1;

        if (looksLikeEmail || ContainsLongDigitSequence(value))
        {
            return "[redacted]";
        }

        return value.Length > 512 ? value[..512] : value;
    }

    internal static JsonNode? PropertyValue(StyledElement element, AvaloniaProperty property, object? value)
    {
        var isInputText = element is TextBox
            && (property.Name.Contains("Text", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));

        if (isInputText || Label(property.Name) == "[redacted]")
        {
            return JsonValue.Create("[redacted]");
        }

        return Value(value);
    }

    internal static JsonNode? Value(object? value)
    {
        return value switch
        {
            null => null,
            string text => JsonValue.Create(Label(text)),
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            float number when float.IsFinite(number) => JsonValue.Create(number),
            double number when double.IsFinite(number) => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            Enum enumeration => JsonValue.Create(enumeration.ToString()),
            Rect rectangle => JsonValue.Create(rectangle.ToString()),
            Size size => JsonValue.Create(size.ToString()),
            Point point => JsonValue.Create(point.ToString()),
            Vector vector => JsonValue.Create(vector.ToString()),
            Thickness thickness => JsonValue.Create(thickness.ToString()),
            CornerRadius radius => JsonValue.Create(radius.ToString()),
            Color color => JsonValue.Create(color.ToString()),
            _ => new JsonObject { ["type"] = value.GetType().FullName }
        };
    }

    internal static object? ParseScalar(string json, Type propertyType)
    {
        JsonNode? value;

        try
        {
            value = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException("The property value must be JSON.", exception);
        }

        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (value is null)
        {
            if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null)
            {
                return null;
            }

            throw new ArgumentException("This property does not accept null.");
        }

        if (value is not JsonValue scalar)
        {
            throw new ArgumentException("Only scalar property values are supported.");
        }

        if (type == typeof(string) && scalar.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (type == typeof(bool) && scalar.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        var isEnumText = type.IsEnum
            && scalar.TryGetValue<string>(out var enumText)
            && Enum.TryParse(type, enumText, true, out var enumValue);

        if (isEnumText)
        {
            // Disallow numeric enum inputs and undefined values.
            var name = scalar.GetValue<string>();

            if (Enum.GetNames(type).Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return Enum.Parse(type, name, true);
            }
        }

        var numericTypes = new[] { typeof(byte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) };

        if (numericTypes.Contains(type))
        {
            try
            {
                var raw = scalar.ToJsonString();
                var finite = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && double.IsFinite(number);

                if (finite)
                {
                    return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new ArgumentException("The numeric value is outside the property's supported range.", exception);
            }
        }

        throw new ArgumentException($"The supplied value is not supported for {propertyType.FullName}. Use a typed app tool for complex properties.");
    }

    private static bool ContainsLongDigitSequence(string value)
    {
        var digitCount = 0;

        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                digitCount++;

                if (digitCount >= 10)
                {
                    return true;
                }
            }
            else if (character is not (' ' or '-' or '(' or ')' or '.'))
            {
                digitCount = 0;
            }
        }

        return false;
    }
}
