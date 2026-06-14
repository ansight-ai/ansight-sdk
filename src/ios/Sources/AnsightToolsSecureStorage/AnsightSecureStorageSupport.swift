import AnsightKit
import Foundation

internal enum AnsightSecureStorageSupport {
    static func getValue(
        options: AnsightSecureStorageToolsOptions,
        arguments: [String: String],
        backend: (any AnsightSecureStorageBackend)? = nil
    ) throws -> JSONValue {
        let key = try requiredString(arguments, key: "key")
        try ensureKeyAllowed(options: options, key: key)

        let result = try (backend ?? AnsightKeychainSecureStorageBackend(options: options)).getValue(key: key)
        return .object([
            "store": .string(result.store),
            "key": .string(result.key),
            "exists": .bool(result.exists),
            "value": result.value.map(JSONValue.string) ?? .null,
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func setValue(
        options: AnsightSecureStorageToolsOptions,
        arguments: [String: String],
        backend: (any AnsightSecureStorageBackend)? = nil
    ) throws -> JSONValue {
        let key = try requiredString(arguments, key: "key")
        try ensureKeyAllowed(options: options, key: key)

        let value = try requiredString(arguments, key: "value")
        let result = try (backend ?? AnsightKeychainSecureStorageBackend(options: options)).setValue(key: key, value: value)
        return .object([
            "store": .string(result.store),
            "key": .string(result.key),
            "updated": .bool(result.updated),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func removeKey(
        options: AnsightSecureStorageToolsOptions,
        arguments: [String: String],
        backend: (any AnsightSecureStorageBackend)? = nil
    ) throws -> JSONValue {
        let key = try requiredString(arguments, key: "key")
        try ensureKeyAllowed(options: options, key: key)

        let result = try (backend ?? AnsightKeychainSecureStorageBackend(options: options)).removeKey(key: key)
        return .object([
            "store": .string(result.store),
            "key": .string(result.key),
            "removed": .bool(result.removed),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func resolveAppleService(options: AnsightSecureStorageToolsOptions) -> String {
        if let service = options.appleService {
            return service
        }

        return Bundle.main.bundleIdentifier ?? "ansight.secure_storage"
    }

    private static func requiredString(_ arguments: [String: String], key: String) throws -> String {
        guard let rawValue = arguments[key] else {
            throw AnsightSecureStorageToolError.invalidArgument("The argument '\(key)' is required.")
        }

        let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else {
            throw AnsightSecureStorageToolError.invalidArgument("The argument '\(key)' is required.")
        }

        return value
    }

    private static func ensureKeyAllowed(options: AnsightSecureStorageToolsOptions, key: String) throws {
        guard isKeyAllowed(options: options, key: key) else {
            throw AnsightSecureStorageToolError.notAllowed(
                "The secure storage key '\(key)' is not allowed by the current registration."
            )
        }
    }

    private static func isKeyAllowed(options: AnsightSecureStorageToolsOptions, key: String) -> Bool {
        if options.allowedKeys.isEmpty && options.allowedKeyPrefixes.isEmpty {
            return false
        }

        if options.allowedKeys.contains(key) {
            return true
        }

        return options.allowedKeyPrefixes.contains { key.hasPrefix($0) }
    }
}
