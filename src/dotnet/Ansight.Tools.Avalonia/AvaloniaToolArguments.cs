using System.Globalization;
using System.Text.Json.Nodes;

namespace Ansight.Tools.Avalonia;

internal static class AvaloniaToolArguments
{
    internal static string? Get(IReadOnlyDictionary<string, string> arguments, string key)
    {
        return arguments.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    internal static string Required(IReadOnlyDictionary<string, string> arguments, string key)
    {
        return Get(arguments, key) ?? throw new ArgumentException($"'{key}' is required.");
    }

    internal static int Integer(IReadOnlyDictionary<string, string> arguments, string key, int fallback, int minimum, int maximum)
    {
        var text = Get(arguments, key);

        if (text is null)
        {
            return fallback;
        }

        var isValid = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value >= minimum
            && value <= maximum;

        if (!isValid)
        {
            throw new ArgumentException($"'{key}' must be an integer between {minimum} and {maximum}.");
        }

        return value;
    }

    internal static bool Boolean(IReadOnlyDictionary<string, string> arguments, string key, bool fallback = false)
    {
        var text = Get(arguments, key);

        if (text is null)
        {
            return fallback;
        }

        return bool.TryParse(text, out var value) ? value : throw new ArgumentException($"'{key}' must be a boolean.");
    }

    internal static JsonNode? Json(IReadOnlyDictionary<string, string> arguments, string key)
    {
        var text = Get(arguments, key);

        try
        {
            return text is null ? null : JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException($"'{key}' must contain valid JSON.", exception);
        }
    }
}
