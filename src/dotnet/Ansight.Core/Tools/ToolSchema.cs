namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// JSON-schema-like descriptor used to declare tool arguments and result payload shapes.
/// </summary>
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

    /// <summary>
    /// Schema value type.
    /// </summary>
    public ToolSchemaType Type { get; }

    /// <summary>
    /// Optional human-readable description of the schema or property.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Object properties for object schemas.
    /// </summary>
    public IReadOnlyDictionary<string, ToolSchema> Properties { get; }

    /// <summary>
    /// Required property names for object schemas.
    /// </summary>
    public IReadOnlyList<string> Required { get; }

    /// <summary>
    /// Item schema for array schemas.
    /// </summary>
    public ToolSchema? Items { get; }

    /// <summary>
    /// Allowed values for string enums.
    /// </summary>
    public IReadOnlyList<string> EnumValues { get; }

    /// <summary>
    /// Indicates whether object schemas allow properties not declared in <see cref="Properties"/>.
    /// </summary>
    public bool AdditionalProperties { get; }

    /// <summary>
    /// Indicates whether the schema also allows <see langword="null"/>.
    /// </summary>
    public bool Nullable { get; }

    /// <summary>
    /// Optional format hint such as <c>date-time</c>.
    /// </summary>
    public string? Format { get; }

    /// <summary>
    /// Creates an object schema.
    /// </summary>
    /// <param name="description">Optional description of the object.</param>
    /// <param name="properties">Declared object properties.</param>
    /// <param name="required">Required property names.</param>
    /// <param name="additionalProperties"><see langword="true"/> to allow undeclared object properties.</param>
    /// <param name="nullable"><see langword="true"/> to allow <see langword="null"/>.</param>
    /// <returns>An object schema descriptor.</returns>
    public static ToolSchema Object(
        string? description = null,
        IReadOnlyDictionary<string, ToolSchema>? properties = null,
        IReadOnlyList<string>? required = null,
        bool additionalProperties = false,
        bool nullable = false)
        => new(ToolSchemaType.Object, description, properties, required, null, null, additionalProperties, nullable, null);

    /// <summary>
    /// Creates an array schema.
    /// </summary>
    /// <param name="items">Schema for each array item.</param>
    /// <param name="description">Optional description of the array.</param>
    /// <param name="nullable"><see langword="true"/> to allow <see langword="null"/>.</param>
    /// <returns>An array schema descriptor.</returns>
    public static ToolSchema Array(
        ToolSchema items,
        string? description = null,
        bool nullable = false)
        => new(ToolSchemaType.Array, description, null, null, items, null, additionalProperties: false, nullable, null);

    /// <summary>
    /// Creates a string schema.
    /// </summary>
    /// <param name="description">Optional description of the string.</param>
    /// <param name="enumValues">Optional set of allowed string values.</param>
    /// <param name="nullable"><see langword="true"/> to allow <see langword="null"/>.</param>
    /// <param name="format">Optional format hint such as <c>date-time</c>.</param>
    /// <returns>A string schema descriptor.</returns>
    public static ToolSchema String(
        string? description = null,
        IReadOnlyList<string>? enumValues = null,
        bool nullable = false,
        string? format = null)
        => new(ToolSchemaType.String, description, null, null, null, enumValues, additionalProperties: false, nullable, format);

    /// <summary>
    /// Creates an integer schema.
    /// </summary>
    /// <param name="description">Optional description of the integer.</param>
    /// <param name="nullable"><see langword="true"/> to allow <see langword="null"/>.</param>
    /// <returns>An integer schema descriptor.</returns>
    public static ToolSchema Integer(string? description = null, bool nullable = false)
        => new(ToolSchemaType.Integer, description, null, null, null, null, additionalProperties: false, nullable, null);

    /// <summary>
    /// Creates a number schema.
    /// </summary>
    /// <param name="description">Optional description of the number.</param>
    /// <param name="nullable"><see langword="true"/> to allow <see langword="null"/>.</param>
    /// <returns>A number schema descriptor.</returns>
    public static ToolSchema Number(string? description = null, bool nullable = false)
        => new(ToolSchemaType.Number, description, null, null, null, null, additionalProperties: false, nullable, null);

    /// <summary>
    /// Creates a boolean schema.
    /// </summary>
    /// <param name="description">Optional description of the boolean.</param>
    /// <param name="nullable"><see langword="true"/> to allow <see langword="null"/>.</param>
    /// <returns>A boolean schema descriptor.</returns>
    public static ToolSchema Boolean(string? description = null, bool nullable = false)
        => new(ToolSchemaType.Boolean, description, null, null, null, null, additionalProperties: false, nullable, null);

    /// <summary>
    /// Converts the schema into a JSON object suitable for catalogs and protocol payloads.
    /// </summary>
    /// <returns>JSON representation of the schema.</returns>
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

    /// <summary>
    /// Validates that the schema is structurally consistent.
    /// </summary>
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
