import Foundation

public struct AnsightChannel: Sendable, Codable, Hashable {
    public let id: Int
    public let name: String
    public let color: String?

    public var colorHex: String? { color }

    public init(id: Int, name: String, color: String? = nil) {
        self.id = id
        self.name = name
        self.color = color
    }

    public init(id: Int, name: String, colorHex: String?) {
        self.init(id: id, name: name, color: colorHex)
    }
}
