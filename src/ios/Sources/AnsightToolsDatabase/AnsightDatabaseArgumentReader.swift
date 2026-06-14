import Foundation

internal enum AnsightDatabaseArgumentReader {
    static func string(_ arguments: [String: String], key: String) -> String? {
        guard let rawValue = arguments[key] else {
            return nil
        }

        let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    static func requiredString(_ arguments: [String: String], key: String) throws -> String {
        guard let value = string(arguments, key: key) else {
            throw AnsightDatabaseToolError.invalidArgument("The argument '\(key)' is required.")
        }

        return value
    }

    static func integer(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int,
        minimum: Int,
        maximum: Int
    ) throws -> Int {
        guard let rawValue = string(arguments, key: key) else {
            return defaultValue
        }

        guard let value = Int(rawValue) else {
            throw AnsightDatabaseToolError.invalidArgument("The argument '\(key)' must be an integer.")
        }

        return min(max(value, minimum), maximum)
    }

    static func boolean(_ arguments: [String: String], key: String, defaultValue: Bool) throws -> Bool {
        guard let rawValue = string(arguments, key: key) else {
            return defaultValue
        }

        switch rawValue.lowercased() {
        case "true", "1":
            return true
        case "false", "0":
            return false
        default:
            throw AnsightDatabaseToolError.invalidArgument("The argument '\(key)' must be a boolean.")
        }
    }
}
