import Foundation

internal struct AnsightToolSchemaValidationError: Sendable, Equatable {
    let path: String
    let code: String
    let message: String

    var jsonValue: JSONValue {
        .object([
            "path": .string(path),
            "code": .string(code),
            "message": .string(message),
        ])
    }
}

internal enum AnsightToolSchemaValidator {
    static func validate(schema: AnsightToolSchema, value: JSONValue) -> [AnsightToolSchemaValidationError] {
        var errors: [AnsightToolSchemaValidationError] = []
        validate(schema: schema.json, value: value, path: "$", errors: &errors)
        return errors
    }

    static func errorsJSON(_ errors: [AnsightToolSchemaValidationError]) -> JSONValue {
        .object([
            "valid": .bool(errors.isEmpty),
            "errors": .array(errors.map(\.jsonValue)),
        ])
    }

    private static func validate(
        schema: JSONValue,
        value: JSONValue,
        path: String,
        errors: inout [AnsightToolSchemaValidationError]
    ) {
        guard case .object(let descriptor) = schema else { return }
        let allowedTypes = schemaTypes(descriptor["type"])
        if value == .null {
            if !allowedTypes.contains("null") {
                errors.append(.init(path: path, code: "null_not_allowed", message: "The value cannot be null."))
            }
            return
        }

        let expectedType = allowedTypes.first { $0 != "null" }
        switch expectedType {
        case "object":
            guard case .object(let object) = value else {
                typeError(path: path, expected: "object", errors: &errors)
                return
            }
            let properties: [String: JSONValue]
            if case .object(let declared)? = descriptor["properties"] { properties = declared } else { properties = [:] }
            let required: [String]
            if case .array(let names)? = descriptor["required"] {
                required = names.compactMap { if case .string(let name) = $0 { name } else { nil } }
            } else { required = [] }
            required.filter { object[$0] == nil || object[$0] == .null }.forEach { name in
                errors.append(.init(
                    path: "\(path).\(name)",
                    code: "required_property_missing",
                    message: "The required property '\(name)' is missing."
                ))
            }
            let allowsAdditional = descriptor["additionalProperties"] == .bool(true)
            for (name, propertyValue) in object {
                if let propertySchema = properties[name] {
                    validate(schema: propertySchema, value: propertyValue, path: "\(path).\(name)", errors: &errors)
                } else if !allowsAdditional {
                    errors.append(.init(
                        path: "\(path).\(name)",
                        code: "additional_property_not_allowed",
                        message: "The property '\(name)' is not declared by the schema."
                    ))
                }
            }
        case "array":
            guard case .array(let values) = value else {
                typeError(path: path, expected: "array", errors: &errors)
                return
            }
            if let itemSchema = descriptor["items"] {
                for (index, item) in values.enumerated() {
                    validate(schema: itemSchema, value: item, path: "\(path)[\(index)]", errors: &errors)
                }
            }
        case "string":
            guard case .string(let stringValue) = value else {
                typeError(path: path, expected: "string", errors: &errors)
                return
            }
            if case .array(let enumValues)? = descriptor["enum"],
               !enumValues.contains(.string(stringValue)) {
                errors.append(.init(path: path, code: "enum_value_invalid", message: "The value is not in the declared enum."))
            }
        case "integer":
            if case .integer = value {} else { typeError(path: path, expected: "integer", errors: &errors) }
        case "number":
            if case .integer = value {} else if case .number = value {} else {
                typeError(path: path, expected: "number", errors: &errors)
            }
        case "boolean":
            if case .bool = value {} else { typeError(path: path, expected: "boolean", errors: &errors) }
        default:
            break
        }
    }

    private static func schemaTypes(_ value: JSONValue?) -> [String] {
        switch value {
        case .string(let type): return [type]
        case .array(let values): return values.compactMap { if case .string(let type) = $0 { type } else { nil } }
        default: return []
        }
    }

    private static func typeError(
        path: String,
        expected: String,
        errors: inout [AnsightToolSchemaValidationError]
    ) {
        errors.append(.init(path: path, code: "type_mismatch", message: "The value must be a JSON \(expected)."))
    }
}
