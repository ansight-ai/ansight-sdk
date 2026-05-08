namespace Ansight.Tools.Maui;

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class MauiToolHelpers
{
    internal const int DefaultTreeDepth = 8;
    internal const int MaximumTreeDepth = 64;
    internal const int DefaultTreeMaxNodes = 350;
    internal const int MaximumTreeMaxNodes = 5000;
    internal const int DefaultObjectDepth = 1;
    internal const int MaximumObjectDepth = 4;
    internal const int DefaultMaxItems = 16;
    internal const int MaximumMaxItems = 64;
    internal const int DefaultMaxProperties = 32;
    internal const int MaximumMaxProperties = 128;
    internal const int DefaultSearchResults = 32;
    internal const int MaximumSearchResults = 256;
    internal const int MaximumStringLength = 512;
    internal const string RedactedLabel = "[redacted]";
    private static readonly string[] sensitiveLabelKeywords =
    [
        "access token",
        "account number",
        "api key",
        "apikey",
        "auth token",
        "authorization",
        "bearer",
        "card number",
        "credential",
        "credit card",
        "cvc",
        "cvv",
        "mfa",
        "one time",
        "otp",
        "passcode",
        "password",
        "private key",
        "refresh token",
        "routing number",
        "secret",
        "social security",
        "ssn",
        "token"
    ];
    internal static readonly JsonSerializerOptions jsonSerializerOptions = new(JsonSerializerDefaults.Web);

#if !(ANDROID || IOS || MACCATALYST)
    internal static ToolResult CreateUnsupportedResult()
        => ToolResult.Failure(".NET MAUI tools are only supported on Android, iOS, and Mac Catalyst MAUI targets.", errorCode: "maui_platform_unsupported");

#endif

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

    internal static int? GetOptionalInt(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return parsedValue;
    }

    internal static bool GetBoolean(IReadOnlyDictionary<string, string> arguments, string key, bool defaultValue)
    {
        if (!arguments.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (bool.TryParse(rawValue, out var boolValue))
        {
            return boolValue;
        }

        return rawValue switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidOperationException($"The argument '{key}' must be a boolean.")
        };
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

    internal static PropertyInfo? ResolvePublicInstanceProperty(Type type, string propertyName)
    {
        return type
            .GetRuntimeProperties()
            .Where(property => property.GetMethod is { IsStatic: false, IsPublic: true })
            .Where(property => property.GetIndexParameters().Length == 0)
            .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasPublicSetter(PropertyInfo property)
        => property.SetMethod is { IsStatic: false, IsPublic: true };

    internal static string? CreateSafeLabel(string? value)
    {
        var trimmedValue = NullIfWhiteSpace(value?.Trim());
        if (trimmedValue == null)
        {
            return null;
        }

        return LooksSensitiveText(trimmedValue)
            ? RedactedLabel
            : Truncate(trimmedValue);
    }

    internal static string? CreateInputPlaceholderLabel(string? placeholder, bool isSensitiveInput)
    {
        if (isSensitiveInput)
        {
            return RedactedLabel;
        }

        return CreateSafeLabel(placeholder);
    }

    internal static string? CreateSafeNavigationLocation(string? value)
    {
        var trimmedValue = NullIfWhiteSpace(value?.Trim());
        if (trimmedValue == null)
        {
            return null;
        }

        var sensitiveSuffixIndex = trimmedValue.IndexOfAny(['?', '#']);
        if (sensitiveSuffixIndex >= 0)
        {
            trimmedValue = trimmedValue[..sensitiveSuffixIndex];
        }

        return CreateSafeLabel(trimmedValue);
    }

    internal static bool LooksSensitiveText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var keyword in sensitiveLabelKeywords)
        {
            if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return LooksLikeEmailAddress(value) || HasLongDigitSequence(value);
    }

    private static bool LooksLikeEmailAddress(string value)
    {
        var atIndex = value.IndexOf('@');
        if (atIndex <= 0 || atIndex >= value.Length - 3)
        {
            return false;
        }

        var dotIndex = value.IndexOf('.', atIndex + 2);
        return dotIndex > atIndex + 1 && dotIndex < value.Length - 1;
    }

    private static bool HasLongDigitSequence(string value)
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

                continue;
            }

            if (character is not (' ' or '-' or '(' or ')' or '.'))
            {
                digitCount = 0;
            }
        }

        return false;
    }

    internal static JsonObject CreateTypeMetadata(Type type)
    {
        return new JsonObject
        {
            ["name"] = type.Name,
            ["fullName"] = type.FullName ?? type.Name,
            ["namespace"] = type.Namespace,
            ["assemblyName"] = type.Assembly.GetName().Name
        };
    }

    internal static string GetTypeDisplayName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var genericTypeName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tickIndex = genericTypeName.IndexOf('`', StringComparison.Ordinal);
        if (tickIndex >= 0)
        {
            genericTypeName = genericTypeName[..tickIndex];
        }

        return $"{genericTypeName}<{string.Join(", ", type.GetGenericArguments().Select(GetTypeDisplayName))}>";
    }

    internal static string GetTypeShortName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var genericTypeName = type.Name;
        var tickIndex = genericTypeName.IndexOf('`', StringComparison.Ordinal);
        if (tickIndex >= 0)
        {
            genericTypeName = genericTypeName[..tickIndex];
        }

        return $"{genericTypeName}<{string.Join(", ", type.GetGenericArguments().Select(GetTypeShortName))}>";
    }

    internal static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);
    }

    internal static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    internal static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= MaximumStringLength)
        {
            return value;
        }

        return value[..MaximumStringLength];
    }

    internal static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return string.Concat(char.ToLowerInvariant(value[0]), value[1..]);
    }
}
