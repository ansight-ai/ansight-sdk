namespace Ansight.Tools.Maui;

#if ANDROID || IOS || MACCATALYST
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Maui.Controls;

internal static partial class MauiToolHelpers
{
    internal static JsonObject CreateValueSnapshot(
        object? value,
        Type? declaredType,
        int depthRemaining,
        int maxItems,
        int maxProperties)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return CreateValueSnapshotCore(value, declaredType, depthRemaining, maxItems, maxProperties, visited);
    }

    internal static JsonObject CreateValueMetadataSnapshot(object? value, Type? declaredType)
    {
        var runtimeType = value?.GetType();
        var effectiveType = runtimeType ?? declaredType;
        var json = new JsonObject
        {
            ["isNull"] = value == null,
            ["kind"] = GetValueKind(value, effectiveType),
            ["declaredType"] = declaredType == null ? null : CreateTypeMetadata(declaredType),
            ["runtimeType"] = runtimeType == null ? null : CreateTypeMetadata(runtimeType)
        };

        if (value is Element element)
        {
            json["element"] = CreateElementReference(element);
        }

        return json;
    }

    internal static JsonObject CreateResourceDictionarySnapshot(
        object resources,
        string scope,
        object owner,
        bool includeValues,
        bool includeMergedDictionaries,
        int maxEntries)
    {
        var json = new JsonObject
        {
            ["scope"] = scope,
            ["ownerType"] = CreateTypeMetadata(owner.GetType()),
            ["resourceType"] = CreateTypeMetadata(resources.GetType())
        };

        if (owner is Element ownerElement)
        {
            json["owner"] = CreateElementReference(ownerElement);
        }

        var entries = new JsonArray();
        var count = 0;
        foreach (var entry in EnumerateDictionaryEntries(resources))
        {
            if (count >= maxEntries)
            {
                json["entriesTruncated"] = true;
                break;
            }

            var entryJson = new JsonObject
            {
                ["key"] = entry.Key,
                ["valueType"] = entry.Value == null ? null : CreateTypeMetadata(entry.Value.GetType())
            };

            if (includeValues)
            {
                entryJson["stringValue"] = CreateSafeLabel(entry.Value?.ToString());
                entryJson["value"] = CreateValueSnapshot(entry.Value, entry.Value?.GetType(), depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
            }

            entries.Add(entryJson);
            count++;
        }

        json["entries"] = entries;
        json["entryCount"] = count;

        if (includeMergedDictionaries && TryReadPublicProperty(resources, "MergedDictionaries", out var mergedDictionaries, out _) &&
            mergedDictionaries is IEnumerable enumerable)
        {
            var merged = new JsonArray();
            foreach (var dictionary in enumerable)
            {
                if (dictionary == null)
                {
                    continue;
                }

                merged.Add(new JsonObject
                {
                    ["type"] = CreateTypeMetadata(dictionary.GetType()),
                    ["entryCount"] = EnumerateDictionaryEntries(dictionary).Count
                });
            }

            json["mergedDictionaries"] = merged;
        }

        return json;
    }

    internal static IReadOnlyList<ResourceEntry> EnumerateDictionaryEntries(object dictionary)
    {
        var entries = new List<ResourceEntry>();

        if (dictionary is IDictionary nonGenericDictionary)
        {
            foreach (DictionaryEntry entry in nonGenericDictionary)
            {
                entries.Add(new ResourceEntry(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty, entry.Value));
            }

            return entries;
        }

        if (dictionary is not IEnumerable enumerable)
        {
            return entries;
        }

        foreach (var item in enumerable)
        {
            if (item == null)
            {
                continue;
            }

            var itemType = item.GetType();
            var keyProperty = itemType.GetRuntimeProperty("Key");
            var valueProperty = itemType.GetRuntimeProperty("Value");
            if (keyProperty == null || valueProperty == null)
            {
                continue;
            }

            object? key;
            object? value;
            try
            {
                key = keyProperty.GetValue(item);
                value = valueProperty.GetValue(item);
            }
            catch
            {
                continue;
            }

            entries.Add(new ResourceEntry(Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty, value));
        }

        return entries;
    }

    internal static JsonObject CreateValueSnapshotCore(
        object? value,
        Type? declaredType,
        int depthRemaining,
        int maxItems,
        int maxProperties,
        HashSet<object> visited)
    {
        var runtimeType = value?.GetType();
        var effectiveType = runtimeType ?? declaredType;
        var json = new JsonObject
        {
            ["isNull"] = value == null,
            ["kind"] = GetValueKind(value, effectiveType),
            ["declaredType"] = declaredType == null ? null : CreateTypeMetadata(declaredType),
            ["runtimeType"] = runtimeType == null ? null : CreateTypeMetadata(runtimeType)
        };

        if (value == null)
        {
            return json;
        }

        if (value is Element element)
        {
            json["element"] = CreateElementReference(element);
            return json;
        }

        var simpleValue = CreateSimpleJsonValue(value);
        if (simpleValue != null)
        {
            json["value"] = simpleValue;
            json["stringValue"] = Truncate(Convert.ToString(value, CultureInfo.InvariantCulture));
            return json;
        }

        if (depthRemaining <= 0)
        {
            return json;
        }

        if (!effectiveType!.IsValueType && !visited.Add(value))
        {
            json["cycle"] = true;
            return json;
        }

        if (value is IEnumerable enumerable and not string)
        {
            var items = new JsonArray();
            var count = 0;
            var truncated = false;
            foreach (var item in enumerable)
            {
                if (count >= maxItems)
                {
                    truncated = true;
                    break;
                }

                items.Add(CreateValueSnapshotCore(item, item?.GetType(), depthRemaining - 1, maxItems, maxProperties, visited));
                count++;
            }

            json["items"] = items;
            json["truncated"] = truncated;
            return json;
        }

        var properties = new JsonObject();
        var propertyCount = 0;
        foreach (var property in effectiveType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (propertyCount >= maxProperties)
            {
                json["propertiesTruncated"] = true;
                break;
            }

            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
            {
                continue;
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            properties[property.Name] = CreateValueSnapshotCore(propertyValue, property.PropertyType, depthRemaining - 1, maxItems, maxProperties, visited);
            propertyCount++;
        }

        json["properties"] = properties;
        return json;
    }

    internal static string GetValueKind(object? value, Type? type)
    {
        if (value == null)
        {
            return "null";
        }

        type ??= value.GetType();
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (value is Element)
        {
            return "maui_element";
        }

        if (underlyingType == typeof(string) || underlyingType == typeof(char) || underlyingType == typeof(Guid))
        {
            return "string";
        }

        if (underlyingType == typeof(bool))
        {
            return "boolean";
        }

        if (underlyingType.IsEnum)
        {
            return "enum";
        }

        if (IsNumericType(underlyingType))
        {
            return "number";
        }

        if (underlyingType == typeof(DateTime) ||
            underlyingType == typeof(DateTimeOffset) ||
            underlyingType == typeof(TimeSpan))
        {
            return "temporal";
        }

        if (value is IEnumerable and not string)
        {
            return "collection";
        }

        return "object";
    }
}
#endif
