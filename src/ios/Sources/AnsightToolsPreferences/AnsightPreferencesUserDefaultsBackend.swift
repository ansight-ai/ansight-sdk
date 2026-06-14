import Foundation

internal final class AnsightPreferencesUserDefaultsBackend: AnsightPreferencesBackend {
    func listKeys(store: String?) throws -> AnsightPreferenceListKeysResult {
        let resolved = try defaults(for: store)
        let keys = resolved.defaults.dictionaryRepresentation().keys
            .filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

        return AnsightPreferenceListKeysResult(store: resolved.store, keys: Array(keys))
    }

    func getValue(store: String?, key: String) throws -> AnsightPreferenceValueResult {
        let resolved = try defaults(for: store)
        guard let value = resolved.defaults.object(forKey: key) else {
            return AnsightPreferenceValueResult(
                store: resolved.store,
                key: key,
                exists: false,
                value: nil,
                valueKind: nil
            )
        }

        return try valueResult(store: resolved.store, key: key, value: value)
    }

    func setValue(
        store: String?,
        key: String,
        valueKind: AnsightPreferenceValueKind,
        value: String
    ) throws -> AnsightPreferenceWriteResult {
        let resolved = try defaults(for: store)
        switch valueKind {
        case .string:
            resolved.defaults.set(value, forKey: key)
        case .boolean:
            resolved.defaults.set(try parseBoolean(value), forKey: key)
        case .integer:
            resolved.defaults.set(NSNumber(value: try parseInteger(value)), forKey: key)
        case .number:
            resolved.defaults.set(try parseNumber(value), forKey: key)
        case .stringArray:
            resolved.defaults.set(try parseStringArray(value), forKey: key)
        case .unsupported:
            throw AnsightPreferencesToolError.invalidArgument("The value type 'unsupported' is not supported for writing.")
        }

        resolved.defaults.synchronize()
        return AnsightPreferenceWriteResult(
            store: resolved.store,
            key: key,
            valueKind: valueKind,
            updated: true
        )
    }

    func removeKey(store: String?, key: String) throws -> AnsightPreferenceRemoveResult {
        let resolved = try defaults(for: store)
        let removed = resolved.defaults.object(forKey: key) != nil
        if removed {
            resolved.defaults.removeObject(forKey: key)
            resolved.defaults.synchronize()
        }

        return AnsightPreferenceRemoveResult(store: resolved.store, key: key, removed: removed)
    }

    private func defaults(for store: String?) throws -> (defaults: UserDefaults, store: String) {
        let trimmedStore = store?.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmedStore?.isEmpty != false ||
            trimmedStore?.caseInsensitiveCompare("default") == .orderedSame ||
            trimmedStore?.caseInsensitiveCompare("standard") == .orderedSame {
            return (.standard, "standard")
        }

        guard let storeName = trimmedStore,
              let defaults = UserDefaults(suiteName: storeName) else {
            throw AnsightPreferencesToolError.platformUnsupported("The preferences store could not be opened.")
        }

        return (defaults, storeName)
    }

    private func valueResult(store: String, key: String, value: Any) throws -> AnsightPreferenceValueResult {
        if let stringValue = value as? String {
            return AnsightPreferenceValueResult(
                store: store,
                key: key,
                exists: true,
                value: stringValue,
                valueKind: .string
            )
        }

        if let numberValue = value as? NSNumber {
            return numberResult(store: store, key: key, value: numberValue)
        }

        if let stringArray = value as? [String] {
            return AnsightPreferenceValueResult(
                store: store,
                key: key,
                exists: true,
                value: try encodeStringArray(stringArray),
                valueKind: .stringArray
            )
        }

        if let anyArray = value as? [Any], anyArray.allSatisfy({ $0 is String }) {
            return AnsightPreferenceValueResult(
                store: store,
                key: key,
                exists: true,
                value: try encodeStringArray(anyArray.compactMap { $0 as? String }),
                valueKind: .stringArray
            )
        }

        return AnsightPreferenceValueResult(
            store: store,
            key: key,
            exists: true,
            value: String(describing: value),
            valueKind: .unsupported
        )
    }

    private func numberResult(store: String, key: String, value: NSNumber) -> AnsightPreferenceValueResult {
        if CFGetTypeID(value) == CFBooleanGetTypeID() {
            return AnsightPreferenceValueResult(
                store: store,
                key: key,
                exists: true,
                value: value.boolValue ? "true" : "false",
                valueKind: .boolean
            )
        }

        let objectiveCType = String(cString: value.objCType)
        if objectiveCType == "f" || objectiveCType == "d" {
            return AnsightPreferenceValueResult(
                store: store,
                key: key,
                exists: true,
                value: String(value.doubleValue),
                valueKind: .number
            )
        }

        return AnsightPreferenceValueResult(
            store: store,
            key: key,
            exists: true,
            value: String(value.int64Value),
            valueKind: .integer
        )
    }

    private func parseBoolean(_ value: String) throws -> Bool {
        if let bool = Bool(value) {
            return bool
        }

        switch value.trimmingCharacters(in: .whitespacesAndNewlines) {
        case "1":
            return true
        case "0":
            return false
        default:
            throw AnsightPreferencesToolError.invalidArgument("The provided boolean value is invalid.")
        }
    }

    private func parseInteger(_ value: String) throws -> Int64 {
        guard let integer = Int64(value) else {
            throw AnsightPreferencesToolError.invalidArgument("The provided integer value is invalid.")
        }

        return integer
    }

    private func parseNumber(_ value: String) throws -> Double {
        guard let number = Double(value) else {
            throw AnsightPreferencesToolError.invalidArgument("The provided numeric value is invalid.")
        }

        return number
    }

    private func parseStringArray(_ value: String) throws -> [String] {
        guard let data = value.data(using: .utf8),
              let array = try JSONSerialization.jsonObject(with: data, options: []) as? [String] else {
            throw AnsightPreferencesToolError.invalidArgument("The 'string_array' value must be a JSON string array.")
        }

        return array
    }

    private func encodeStringArray(_ value: [String]) throws -> String {
        let data = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
        guard let result = String(data: data, encoding: .utf8) else {
            throw AnsightPreferencesToolError.invalidArgument("The string array could not be encoded.")
        }

        return result
    }
}
