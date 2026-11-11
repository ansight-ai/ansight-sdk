import Foundation

public struct AnsightMetricGroup: Sendable, Codable, Hashable {
    public let id: String
    public let name: String

    public init(id: String, name: String) {
        self.id = id.trimmingCharacters(in: .whitespacesAndNewlines)
        self.name = name.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func validated() throws -> AnsightMetricGroup {
        guard !id.isEmpty, !name.isEmpty else {
            throw RuntimeError.invalidInput("Metric group id and name must not be blank.")
        }
        return self
    }

    var jsonValue: JSONValue {
        .object(["id": .string(id), "name": .string(name)])
    }

    private enum CodingKeys: String, CodingKey { case id, name }

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        self.init(id: try values.decode(String.self, forKey: .id),
                  name: try values.decode(String.self, forKey: .name))
        _ = try validated()
    }
}

public enum AnsightMetricGroups {
    public static let memory = AnsightMetricGroup(id: "memory", name: "Memory")
    public static let rendering = AnsightMetricGroup(id: "rendering", name: "Rendering")
    public static let layout = AnsightMetricGroup(id: "layout", name: "Layout")
    public static let runtime = AnsightMetricGroup(id: "runtime", name: "Runtime")
    public static let device = AnsightMetricGroup(id: "device", name: "Device")
    public static let network = AnsightMetricGroup(id: "network", name: "Network")
}

public struct AnsightChannel: Sendable, Codable, Hashable {
    public let id: Int
    public let name: String
    public let color: String?
    public let unit: String?
    public let type: String
    public let source: String?
    public let group: String?
    public let kind: String?
    public let metricGroup: AnsightMetricGroup?

    public var colorHex: String? { color }

    var jsonValue: JSONValue {
        .object([
            "id": .integer(Int64(id)),
            "name": .string(name),
            "unit": unit.map(JSONValue.string) ?? .null,
            "type": .string(type),
            "color": color.map(JSONValue.string) ?? .null,
            "source": source.map(JSONValue.string) ?? .null,
            "group": group.map(JSONValue.string) ?? .null,
            "metricGroup": metricGroup?.jsonValue ?? .null,
            "kind": kind.map(JSONValue.string) ?? .null,
        ])
    }

    public init(
        id: Int,
        name: String,
        color: String? = nil,
        unit: String? = nil,
        type: String = "custom",
        source: String? = nil,
        group: String? = nil,
        kind: String? = nil,
        metricGroup: AnsightMetricGroup? = nil
    ) {
        self.id = id
        self.name = name
        self.color = color
        self.unit = unit
        self.type = type
        self.source = source
        self.group = metricGroup?.name ?? group
        self.metricGroup = metricGroup
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
        kind: String? = nil,
        metricGroup: AnsightMetricGroup? = nil
    ) {
        self.init(id: id, name: name, color: colorHex, unit: unit, type: type, source: source, group: group, kind: kind, metricGroup: metricGroup)
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
        case metricGroup
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(Int.self, forKey: .id)
        name = try container.decode(String.self, forKey: .name)
        color = try container.decodeIfPresent(String.self, forKey: .color)
        unit = try container.decodeIfPresent(String.self, forKey: .unit)
        type = try container.decodeIfPresent(String.self, forKey: .type) ?? "custom"
        source = try container.decodeIfPresent(String.self, forKey: .source)
        metricGroup = try container.decodeIfPresent(AnsightMetricGroup.self, forKey: .metricGroup)
        group = try metricGroup?.name ?? container.decodeIfPresent(String.self, forKey: .group)
        kind = try container.decodeIfPresent(String.self, forKey: .kind)
    }
}
