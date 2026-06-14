import Foundation

public struct AnsightToolSchema: Sendable, Codable, Equatable {
    public static let emptyObject = AnsightToolSchema(json: .object([:]))

    public let json: JSONValue

    public init(json: JSONValue = .object([:])) {
        self.json = json
    }
}
