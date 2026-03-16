namespace Ansight.Tools;

using System.Text.Json.Nodes;

public sealed class ToolSchema
{
    private ToolSchema(
        ToolSchemaType type,
        string? description,
        IReadOnlyDictionary<string, ToolSchema>? properties,
        IReadOnlyList<string>? required,
        ToolSchema? items,
        IReadOnlyList<string>? enumValues,
        bool additionalProperties,
        bool nullable,
        string? format)
    {
        Type = type;
        Description = description;
        Properties = properties ?? new Dictionary<string, ToolSchema>();
        Required = required ?? System.Array.Empty<string>();
        Items = items;
        EnumValues = enumValues ?? System.Array.Empty<string>();
        AdditionalProperties = additionalProperties;
        Nullable = nullable;
        Format = format;
    }

    public ToolSchemaType Type { get; }

    public string? Description { get; }

    public IReadOnlyDictionary<string, ToolSchema> Properties { get; }

    public IReadOnlyList<string> Required { get; }

    public ToolSchema? Items { get; }

    public IReadOnlyList<string> EnumValues { get; }

    public bool AdditionalProperties { get; }

    public bool Nullable { get; }

    public string? Format { get; }

    public static ToolSchema Object(
        string? description = null,
        IReadOnlyDictionary<string, ToolSchema>? properties = null,
        IReadOnlyList<string>? required = null,
        bool additionalProperties = false,
        bool nullable = false)
        => new(ToolSchemaType.Object, description, properties, required, null, null, additionalProperties, nullable, null);

    public static ToolSchema Array(
        ToolSchema items,
        string? description = null,
        bool nullable = false)
        => new(ToolSchemaType.Array, description, null, null, items, null, additionalProperties: false, nullable, null);

    public static ToolSchema String(
        string? description = null,
        IReadOnlyList<string>? enumValues = null,
        bool nullable = false,
        string? format = null)
        => new(ToolSchemaType.String, description, null, null, null, enumValues, additionalProperties: false, nullable, format);

    public static ToolSchema Integer(string? description = null, bool nullable = false)
        => new(ToolSchemaType.Integer, description, null, null, null, null, additionalProperties: false, nullable, null);

    public static ToolSchema Number(string? description = null, bool nullable = false)
        => new(ToolSchemaType.Number, description, null, null, null, null, additionalProperties: false, nullable, null);

    public static ToolSchema Boolean(string? description = null, bool nullable = false)
        => new(ToolSchemaType.Boolean, description, null, null, null, null, additionalProperties: false, nullable, null);

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["type"] = Nullable ? new JsonArray(ToJsonType(Type), "null") : ToJsonType(Type),
            ["additionalProperties"] = AdditionalProperties
        };

        if (!string.IsNullOrWhiteSpace(Description))
        {
            json["description"] = Description;
        }

        if (!string.IsNullOrWhiteSpace(Format))
        {
            json["format"] = Format;
        }

        if (EnumValues.Count > 0)
        {
            var enumArray = new JsonArray();
            foreach (var enumValue in EnumValues)
            {
                enumArray.Add(enumValue);
            }

            json["enum"] = enumArray;
        }

        if (Items != null)
        {
            json["items"] = Items.ToJson();
        }

        if (Properties.Count > 0)
        {
            var propertiesJson = new JsonObject();
            foreach (var property in Properties)
            {
                propertiesJson[property.Key] = property.Value.ToJson();
            }

            json["properties"] = propertiesJson;
        }

        if (Required.Count > 0)
        {
            var requiredJson = new JsonArray();
            foreach (var requiredProperty in Required)
            {
                requiredJson.Add(requiredProperty);
            }

            json["required"] = requiredJson;
        }

        return json;
    }

    public void Validate()
    {
        if (Type == ToolSchemaType.Array && Items == null)
        {
            throw new InvalidOperationException("Array schemas must declare an item schema.");
        }

        foreach (var property in Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Key))
            {
                throw new InvalidOperationException("Schema property names must be non-empty.");
            }

            property.Value.Validate();
        }

        Items?.Validate();
    }

    private static string ToJsonType(ToolSchemaType type) => type switch
    {
        ToolSchemaType.Object => "object",
        ToolSchemaType.Array => "array",
        ToolSchemaType.String => "string",
        ToolSchemaType.Integer => "integer",
        ToolSchemaType.Number => "number",
        ToolSchemaType.Boolean => "boolean",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
