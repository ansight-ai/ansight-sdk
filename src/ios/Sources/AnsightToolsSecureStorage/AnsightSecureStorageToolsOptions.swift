import Foundation

public struct AnsightSecureStorageToolsOptions: Sendable, Codable, Equatable {
    public static let `default` = AnsightSecureStorageToolsOptions()

    public let appleService: String?
    public let allowedKeys: [String]
    public let allowedKeyPrefixes: [String]

    public init(
        appleService: String? = nil,
        allowedKeys: [String] = [],
        allowedKeyPrefixes: [String] = []
    ) {
        self.appleService = Self.normalized(appleService)
        self.allowedKeys = allowedKeys.compactMap(Self.normalized)
        self.allowedKeyPrefixes = allowedKeyPrefixes.compactMap(Self.normalized)
    }

    public static func createBuilder() -> AnsightSecureStorageToolsOptionsBuilder {
        AnsightSecureStorageToolsOptionsBuilder()
    }

    private static func normalized(_ value: String?) -> String? {
        guard let value else {
            return nil
        }

        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}
