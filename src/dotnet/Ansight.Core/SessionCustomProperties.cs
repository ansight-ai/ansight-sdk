using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Pairing;

namespace Ansight;

/// <summary>
/// Stores custom session properties grouped by a caller-defined group and key.
/// </summary>
public sealed class SessionCustomProperties
{
    private const int MaximumGroupLength = 128;
    private const int MaximumKeyLength = 128;

    private readonly Lock propertyLock = new();
    private readonly Dictionary<string, Dictionary<string, JsonNode?>> properties = new(StringComparer.Ordinal);

    /// <summary>
    /// Indicates whether any custom properties are currently registered.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (propertyLock)
            {
                return properties.Count == 0;
            }
        }
    }

    /// <summary>
    /// Registers or replaces a custom session property.
    /// </summary>
    /// <param name="group">Logical property group.</param>
    /// <param name="key">Property key within the group.</param>
    /// <param name="value">Scalar JSON value to send for the property.</param>
    /// <returns>The current property collection.</returns>
    public SessionCustomProperties Register(string group, string key, object? value)
    {
        var normalizedGroup = NormalizeName(group, nameof(group), MaximumGroupLength);
        var normalizedKey = NormalizeName(key, nameof(key), MaximumKeyLength);
        var valueNode = CreateValueNode(value);

        lock (propertyLock)
        {
            if (!properties.TryGetValue(normalizedGroup, out var groupProperties))
            {
                groupProperties = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
                properties[normalizedGroup] = groupProperties;
            }

            groupProperties[normalizedKey] = valueNode;
        }

        return this;
    }

    /// <summary>
    /// Removes a registered custom session property.
    /// </summary>
    /// <param name="group">Logical property group.</param>
    /// <param name="key">Property key within the group.</param>
    /// <returns><see langword="true"/> when a property was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string group, string key)
    {
        var normalizedGroup = NormalizeName(group, nameof(group), MaximumGroupLength);
        var normalizedKey = NormalizeName(key, nameof(key), MaximumKeyLength);

        lock (propertyLock)
        {
            if (!properties.TryGetValue(normalizedGroup, out var groupProperties) ||
                !groupProperties.Remove(normalizedKey))
            {
                return false;
            }

            if (groupProperties.Count == 0)
            {
                properties.Remove(normalizedGroup);
            }

            return true;
        }
    }

    /// <summary>
    /// Removes all registered custom session properties.
    /// </summary>
    public void Clear()
    {
        lock (propertyLock)
        {
            properties.Clear();
        }
    }

    /// <summary>
    /// Creates a detached copy of the current properties.
    /// </summary>
    /// <returns>A cloned property collection.</returns>
    public SessionCustomProperties Clone()
    {
        var clone = new SessionCustomProperties();
        clone.MergeFrom(this);
        return clone;
    }

    internal void MergeFrom(SessionCustomProperties? source)
    {
        if (source is null)
        {
            return;
        }

        var sourceJson = source.ToJsonObject();
        lock (propertyLock)
        {
            foreach (var group in sourceJson)
            {
                if (group.Value is not JsonObject groupObject)
                {
                    continue;
                }

                if (!properties.TryGetValue(group.Key, out var groupProperties))
                {
                    groupProperties = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
                    properties[group.Key] = groupProperties;
                }

                foreach (var property in groupObject)
                {
                    groupProperties[property.Key] = property.Value?.DeepClone();
                }
            }
        }
    }

    internal JsonObject ToJsonObject()
    {
        lock (propertyLock)
        {
            var root = new JsonObject();
            foreach (var group in properties)
            {
                var groupObject = new JsonObject();
                foreach (var property in group.Value)
                {
                    groupObject[property.Key] = property.Value?.DeepClone();
                }

                root[group.Key] = groupObject;
            }

            return root;
        }
    }

    private static string NormalizeName(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot be longer than {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static JsonNode? CreateValueNode(object? value)
    {
        if (value is null)
        {
            return null;
        }

        JsonNode? node = value switch
        {
            JsonNode jsonNode => jsonNode.DeepClone(),
            JsonElement jsonElement => JsonNode.Parse(jsonElement.GetRawText()),
            JsonDocument jsonDocument => JsonNode.Parse(jsonDocument.RootElement.GetRawText()),
            _ => JsonSerializer.SerializeToNode(value, value.GetType(), PairingJson.Compact)
        };

        if (node is JsonObject or JsonArray)
        {
            throw new ArgumentException("Custom property values must be scalar JSON values.", nameof(value));
        }

        return node;
    }
}
