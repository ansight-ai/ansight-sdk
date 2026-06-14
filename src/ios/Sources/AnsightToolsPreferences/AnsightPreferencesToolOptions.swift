import Foundation

public struct AnsightPreferencesToolOptions: Sendable, Codable, Equatable {
    public static let `default` = AnsightPreferencesToolOptions()

    public let defaultStore: String?
    public let allowedStores: [String]
    public let allowedKeys: [String]
    public let allowedKeyPrefixes: [String]

    public init(
        defaultStore: String? = nil,
        allowedStores: [String] = [],
        allowedKeys: [String] = [],
        allowedKeyPrefixes: [String] = []
    ) {
        self.defaultStore = Self.normalized(defaultStore)
        self.allowedStores = allowedStores.compactMap(Self.normalized)
        self.allowedKeys = allowedKeys.compactMap(Self.normalized)
        self.allowedKeyPrefixes = allowedKeyPrefixes.compactMap(Self.normalized)
    }

    private static func normalized(_ value: String?) -> String? {
        guard let value else {
            return nil
        }

        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}
