namespace Ansight.Tools;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// One machine-readable JSON-schema validation error.
/// </summary>
public sealed record ToolSchemaValidationError(
    string Path,
    string Code,
    string Message);

/// <summary>
/// Result of validating a JSON value against a <see cref="ToolSchema"/>.
/// </summary>
public sealed class ToolSchemaValidationResult
{
    internal ToolSchemaValidationResult(IReadOnlyList<ToolSchemaValidationError> errors)
    {
        Errors = errors;
    }

    /// <summary>
    /// Indicates whether the value satisfies the declared schema.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Validation errors in deterministic traversal order.
    /// </summary>
    public IReadOnlyList<ToolSchemaValidationError> Errors { get; }

    /// <summary>
    /// Converts the validation result to protocol JSON.
    /// </summary>
    public JsonObject ToJson()
    {
        var errors = new JsonArray();
        foreach (var error in Errors)
        {
            errors.Add(new JsonObject
            {
                ["path"] = error.Path,
                ["code"] = error.Code,
                ["message"] = error.Message
            });
        }

        return new JsonObject
        {
            ["valid"] = IsValid,
            ["errors"] = errors
        };
    }
}

/// <summary>
/// Validates protocol JSON against Ansight tool schemas.
/// </summary>
public static class ToolSchemaValidator
{
    /// <summary>
    /// Validates a JSON value against the supplied schema.
    /// </summary>
    public static ToolSchemaValidationResult Validate(ToolSchema schema, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var errors = new List<ToolSchemaValidationError>();
        ValidateValue(schema, value, "$", errors);
        return new ToolSchemaValidationResult(errors);
    }

    private static void ValidateValue(
        ToolSchema schema,
        JsonNode? value,
        string path,
        List<ToolSchemaValidationError> errors)
    {
        if (value is null || value.GetValueKind() == JsonValueKind.Null)
        {
            if (!schema.Nullable)
            {
                errors.Add(new ToolSchemaValidationError(path, "null_not_allowed", "The value cannot be null."));
            }

            return;
        }

        switch (schema.Type)
        {
            case ToolSchemaType.Object:
                ValidateObject(schema, value, path, errors);
                break;
            case ToolSchemaType.Array:
                ValidateArray(schema, value, path, errors);
                break;
            case ToolSchemaType.String:
                ValidateString(schema, value, path, errors);
                break;
            case ToolSchemaType.Integer:
                if (!IsInteger(value))
                {
                    AddTypeError(path, "integer", errors);
                }
                break;
            case ToolSchemaType.Number:
                if (!IsNumber(value))
                {
                    AddTypeError(path, "number", errors);
                }
                break;
            case ToolSchemaType.Boolean:
                if (value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                {
                    AddTypeError(path, "boolean", errors);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(schema), schema.Type, null);
        }
    }

    private static void ValidateObject(
        ToolSchema schema,
        JsonNode value,
        string path,
        List<ToolSchemaValidationError> errors)
    {
        if (value is not JsonObject jsonObject)
        {
            AddTypeError(path, "object", errors);
            return;
        }

        foreach (var requiredProperty in schema.Required)
        {
            if (!jsonObject.ContainsKey(requiredProperty) || jsonObject[requiredProperty] is null)
            {
                errors.Add(new ToolSchemaValidationError(
                    AppendProperty(path, requiredProperty),
                    "required_property_missing",
                    $"The required property '{requiredProperty}' is missing."));
            }
        }

        foreach (var property in jsonObject)
        {
            if (!schema.Properties.TryGetValue(property.Key, out var propertySchema))
            {
                if (!schema.AdditionalProperties)
                {
                    errors.Add(new ToolSchemaValidationError(
                        AppendProperty(path, property.Key),
                        "additional_property_not_allowed",
                        $"The property '{property.Key}' is not declared by the schema."));
                }

                continue;
            }

            ValidateValue(propertySchema, property.Value, AppendProperty(path, property.Key), errors);
        }
    }

    private static void ValidateArray(
        ToolSchema schema,
        JsonNode value,
        string path,
        List<ToolSchemaValidationError> errors)
    {
        if (value is not JsonArray array)
        {
            AddTypeError(path, "array", errors);
            return;
        }

        if (schema.Items is null)
        {
            return;
        }

        for (var index = 0; index < array.Count; index++)
        {
            ValidateValue(schema.Items, array[index], $"{path}[{index}]", errors);
        }
    }

    private static void ValidateString(
        ToolSchema schema,
        JsonNode value,
        string path,
        List<ToolSchemaValidationError> errors)
    {
        if (value.GetValueKind() != JsonValueKind.String
            || value is not JsonValue jsonValue
            || !jsonValue.TryGetValue<string>(out var stringValue))
        {
            AddTypeError(path, "string", errors);
            return;
        }

        if (schema.EnumValues.Count > 0
            && !schema.EnumValues.Contains(stringValue, StringComparer.Ordinal))
        {
            errors.Add(new ToolSchemaValidationError(
                path,
                "enum_value_invalid",
                $"The value must be one of: {string.Join(", ", schema.EnumValues)}."));
        }

        if (string.Equals(schema.Format, "date-time", StringComparison.Ordinal)
            && !DateTimeOffset.TryParse(
                stringValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            errors.Add(new ToolSchemaValidationError(
                path,
                "format_invalid",
                "The value must be an ISO-8601 date-time."));
        }
    }

    private static bool IsInteger(JsonNode value)
    {
        if (value.GetValueKind() != JsonValueKind.Number || value is not JsonValue jsonValue)
        {
            return false;
        }

        return jsonValue.TryGetValue<int>(out _)
               || jsonValue.TryGetValue<long>(out _)
               || jsonValue.TryGetValue<uint>(out _)
               || jsonValue.TryGetValue<ulong>(out _)
               || jsonValue.TryGetValue<decimal>(out var decimalValue)
               && decimal.Truncate(decimalValue) == decimalValue;
    }

    private static bool IsNumber(JsonNode value)
        => value.GetValueKind() == JsonValueKind.Number;

    private static void AddTypeError(
        string path,
        string expectedType,
        List<ToolSchemaValidationError> errors)
        => errors.Add(new ToolSchemaValidationError(
            path,
            "type_mismatch",
            $"The value must be a JSON {expectedType}."));

    private static string AppendProperty(string path, string propertyName)
        => $"{path}.{propertyName}";
}
