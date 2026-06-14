import Foundation

public struct AnsightScreenRoute: Sendable, Codable, Equatable {
    public var name: String
    public var key: String?
    public var details: [String: String]

    public init(
        name: String,
        key: String? = nil,
        details: [String: String] = [:]
    ) {
        self.name = name
        self.key = key
        self.details = details
    }
}
