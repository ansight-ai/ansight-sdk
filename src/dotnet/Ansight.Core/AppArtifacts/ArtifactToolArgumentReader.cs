namespace Ansight.Artifacts;

using System.Text.Json.Nodes;

internal static class ArtifactToolArgumentReader
{
    internal static string? GetString(IReadOnlyDictionary<string, string> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    internal static int GetInt(
        IReadOnlyDictionary<string, string> arguments,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var text = GetString(arguments, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        if (!int.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"The argument '{key}' must be an integer.");
        }

        return Math.Clamp(value, minimum, maximum);
    }

    internal static IReadOnlyDictionary<string, string> GetNestedStringArguments(IReadOnlyDictionary<string, string> arguments)
    {
        var nestedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var encodedArguments = GetString(arguments, "arguments");
        if (string.IsNullOrWhiteSpace(encodedArguments))
        {
            return nestedArguments;
        }

        var node = JsonNode.Parse(encodedArguments);
        if (node is not JsonObject jsonObject)
        {
            throw new InvalidOperationException("The argument 'arguments' must be a JSON object.");
        }

        foreach (var property in jsonObject)
        {
            if (property.Value == null)
            {
                continue;
            }

            nestedArguments[property.Key] = property.Value.ToJsonString();
            if (property.Value is JsonValue value)
            {
                nestedArguments[property.Key] = value.ToString();
            }
        }

        return nestedArguments;
    }
}
