import AnsightKit
import Foundation

internal enum AnsightVisualTreeArgumentReader {
    static func string(_ arguments: [String: String], key: String) -> String? {
        guard let value = arguments[key]?.trimmingCharacters(in: .whitespacesAndNewlines),
              !value.isEmpty
        else {
            return nil
        }

        return value
    }

    static func requiredString(_ arguments: [String: String], key: String) throws -> String {
        guard let value = string(arguments, key: key) else {
            throw AnsightVisualTreeToolError.invalidArgument("The argument '\(key)' is required.")
        }

        return value
    }

    static func bool(_ arguments: [String: String], key: String, defaultValue: Bool) throws -> Bool {
        guard let value = string(arguments, key: key) else {
            return defaultValue
        }

        if let parsed = Bool(value) {
            return parsed
        }

        switch value {
        case "1":
            return true
        case "0":
            return false
        default:
            throw AnsightVisualTreeToolError.invalidArgument("The argument '\(key)' must be a boolean.")
        }
    }

    static func integer(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int,
        minimum: Int,
        maximum: Int
    ) throws -> Int {
        guard let value = string(arguments, key: key) else {
            return defaultValue
        }

        guard let parsed = Int(value) else {
            throw AnsightVisualTreeToolError.invalidArgument("The argument '\(key)' must be an integer.")
        }

        return min(max(parsed, minimum), maximum)
    }

    static func optionalInteger(
        _ arguments: [String: String],
        key: String,
        minimum: Int,
        maximum: Int
    ) throws -> Int? {
        guard let value = string(arguments, key: key) else {
            return nil
        }

        guard let parsed = Int(value) else {
            throw AnsightVisualTreeToolError.invalidArgument("The argument '\(key)' must be an integer.")
        }

        return min(max(parsed, minimum), maximum)
    }

    static func double(
        _ arguments: [String: String],
        key: String,
        defaultValue: Double,
        minimum: Double,
        maximum: Double
    ) throws -> Double {
        guard let value = try optionalDouble(arguments, key: key) else {
            return defaultValue
        }

        return min(max(value, minimum), maximum)
    }

    static func optionalDouble(_ arguments: [String: String], key: String) throws -> Double? {
        guard let value = string(arguments, key: key) else {
            return nil
        }

        guard let parsed = Double(value), parsed.isFinite else {
            throw AnsightVisualTreeToolError.invalidArgument("The argument '\(key)' must be a number.")
        }

        return parsed
    }

    static func jsonValue(_ rawValue: String) throws -> JSONValue {
        guard let data = rawValue.data(using: .utf8) else {
            throw AnsightVisualTreeToolError.invalidArgument("JSON arguments must be valid UTF-8.")
        }

        do {
            return try JSONDecoder().decode(JSONValue.self, from: data)
        } catch {
            throw AnsightVisualTreeToolError.invalidArgument("JSON argument could not be parsed: \(error.localizedDescription)")
        }
    }
}
