import Foundation

public struct AnsightDatabaseRoot: Sendable, Codable, Equatable {
    public let alias: String
    public let path: String

    public init(alias: String, path: String) {
        self.alias = alias.trimmingCharacters(in: .whitespacesAndNewlines)
        self.path = path.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
