import Foundation

public struct AnsightChannel: Sendable, Codable, Hashable {
    public let id: Int
    public let name: String
    public let color: String?
    public let unit: String?
    public let type: String
    public let source: String?
    public let group: String?
    public let kind: String?

    public var colorHex: String? { color }

    public init(
        id: Int,
        name: String,
        color: String? = nil,
        unit: String? = nil,
        type: String = "custom",
        source: String? = nil,
        group: String? = nil,
        kind: String? = nil
    ) {
        self.id = id
        self.name = name
        self.color = color
        self.unit = unit
        self.type = type
        self.source = source
        self.group = group
        self.kind = kind
    }

    public init(id: Int, name: String, colorHex: String?) {
        self.init(id: id, name: name, color: colorHex)
    }

    public init(
        id: Int,
        name: String,
        colorHex: String?,
        unit: String?,
        type: String = "custom",
        source: String? = nil,
        group: String? = nil,
        kind: String? = nil
    ) {
        self.init(id: id, name: name, color: colorHex, unit: unit, type: type, source: source, group: group, kind: kind)
    }

    private enum CodingKeys: String, CodingKey {
        case id
        case name
        case color
        case unit
        case type
        case source
        case group
        case kind
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(Int.self, forKey: .id)
        name = try container.decode(String.self, forKey: .name)
        color = try container.decodeIfPresent(String.self, forKey: .color)
        unit = try container.decodeIfPresent(String.self, forKey: .unit)
        type = try container.decodeIfPresent(String.self, forKey: .type) ?? "custom"
        source = try container.decodeIfPresent(String.self, forKey: .source)
        group = try container.decodeIfPresent(String.self, forKey: .group)
        kind = try container.decodeIfPresent(String.self, forKey: .kind)
    }
}
