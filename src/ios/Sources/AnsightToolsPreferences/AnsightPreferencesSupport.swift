import AnsightKit
import Foundation

internal enum AnsightPreferencesSupport {
    static func listKeys(
        options: AnsightPreferencesToolOptions,
        arguments: [String: String],
        backend: AnsightPreferencesBackend = AnsightPreferencesUserDefaultsBackend()
    ) throws -> JSONValue {
        let requestedStore = string(arguments, key: "store") ?? options.defaultStore
        let requestedPrefix = string(arguments, key: "prefix")
        let maxResults = try integer(arguments, key: "maxResults", defaultValue: 200, minimum: 1, maximum: 1_000)
        let result = try backend.listKeys(store: requestedStore)
        try ensureStoreAllowed(options: options, store: result.store)

        let matchingKeys = result.keys
            .filter { isKeyAllowed(options: options, key: $0) }
            .filter { key in
                guard let requestedPrefix else {
                    return true
                }

                return key.hasPrefix(requestedPrefix)
            }
            .sorted()

        let keys = Array(matchingKeys.prefix(maxResults))
        return .object([
            "store": .string(result.store),
            "keys": .array(keys.map(JSONValue.string)),
            "truncated": .bool(matchingKeys.count > maxResults),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func getValue(
        options: AnsightPreferencesToolOptions,
        arguments: [String: String],
        backend: AnsightPreferencesBackend = AnsightPreferencesUserDefaultsBackend()
    ) throws -> JSONValue {
        let key = try requiredString(arguments, key: "key")
        try ensureKeyAllowed(options: options, key: key)

        let requestedStore = string(arguments, key: "store") ?? options.defaultStore
        let result = try backend.getValue(store: requestedStore, key: key)
        try ensureStoreAllowed(options: options, store: result.store)

        return .object([
            "store": .string(result.store),
            "key": .string(result.key),
            "exists": .bool(result.exists),
            "value": result.value.map(JSONValue.string) ?? .null,
            "valueType": result.valueKind.map { .string($0.rawValue) } ?? .null,
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func setValue(
        options: AnsightPreferencesToolOptions,
        arguments: [String: String],
        backend: AnsightPreferencesBackend = AnsightPreferencesUserDefaultsBackend()
    ) throws -> JSONValue {
        let key = try requiredString(arguments, key: "key")
        try ensureKeyAllowed(options: options, key: key)

        let value = try requiredString(arguments, key: "value")
        let valueKind = try valueKind(from: try requiredString(arguments, key: "valueType"))
        let requestedStore = string(arguments, key: "store") ?? options.defaultStore
        let result = try backend.setValue(store: requestedStore, key: key, valueKind: valueKind, value: value)
        try ensureStoreAllowed(options: options, store: result.store)

        return .object([
            "store": .string(result.store),
            "key": .string(result.key),
            "valueType": .string(result.valueKind.rawValue),
            "updated": .bool(result.updated),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func removeKey(
        options: AnsightPreferencesToolOptions,
        arguments: [String: String],
        backend: AnsightPreferencesBackend = AnsightPreferencesUserDefaultsBackend()
    ) throws -> JSONValue {
        let key = try requiredString(arguments, key: "key")
        try ensureKeyAllowed(options: options, key: key)

        let requestedStore = string(arguments, key: "store") ?? options.defaultStore
        let result = try backend.removeKey(store: requestedStore, key: key)
        try ensureStoreAllowed(options: options, store: result.store)

        return .object([
            "store": .string(result.store),
            "key": .string(result.key),
            "removed": .bool(result.removed),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    private static func integer(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int,
        minimum: Int,
        maximum: Int
    ) throws -> Int {
        guard let rawValue = string(arguments, key: key) else {
            return defaultValue
        }

        guard let parsedValue = Int(rawValue) else {
            throw AnsightPreferencesToolError.invalidArgument("The argument '\(key)' must be an integer.")
        }

        return min(max(parsedValue, minimum), maximum)
    }

    private static func string(_ arguments: [String: String], key: String) -> String? {
        guard let rawValue = arguments[key] else {
            return nil
        }

        let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }

    private static func requiredString(_ arguments: [String: String], key: String) throws -> String {
        guard let value = string(arguments, key: key) else {
            throw AnsightPreferencesToolError.invalidArgument("The argument '\(key)' is required.")
        }

        return value
    }

    private static func valueKind(from rawValue: String) throws -> AnsightPreferenceValueKind {
        guard let kind = AnsightPreferenceValueKind(rawValue: rawValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()),
              kind != .unsupported else {
            throw AnsightPreferencesToolError.invalidArgument("The value type '\(rawValue)' is not supported.")
        }

        return kind
    }

    private static func ensureStoreAllowed(options: AnsightPreferencesToolOptions, store: String) throws {
        guard !options.allowedStores.isEmpty else {
            return
        }

        if options.allowedStores.contains(where: { $0.caseInsensitiveCompare(store) == .orderedSame }) {
            return
        }

        throw AnsightPreferencesToolError.notAllowed(
            "The preferences store '\(store)' is not allowed by the current registration."
        )
    }

    private static func ensureKeyAllowed(options: AnsightPreferencesToolOptions, key: String) throws {
        guard isKeyAllowed(options: options, key: key) else {
            throw AnsightPreferencesToolError.notAllowed(
                "The preferences key '\(key)' is not allowed by the current registration."
            )
        }

        return
    }

    private static func isKeyAllowed(options: AnsightPreferencesToolOptions, key: String) -> Bool {
        if options.allowedKeys.isEmpty && options.allowedKeyPrefixes.isEmpty {
            return true
        }

        if options.allowedKeys.contains(key) {
            return true
        }

        return options.allowedKeyPrefixes.contains { key.hasPrefix($0) }
    }
}
