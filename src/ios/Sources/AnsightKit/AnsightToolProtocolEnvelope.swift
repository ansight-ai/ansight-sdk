import Foundation

public struct AnsightToolProtocolEnvelope: Sendable, Codable, Equatable {
    private enum CodingKeys: String, CodingKey {
        case type
        case id
        case replyTo
        case sessionId
        case sentAt
        case capability
        case payload
    }

    public let type: String
    public let id: String
    public let replyTo: String?
    public let sessionId: String?
    public let sentAt: String
    public let capability: String?
    public let payload: JSONValue

    public init(
        type: String,
        id: String,
        replyTo: String? = nil,
        sessionId: String? = nil,
        sentAt: String? = nil,
        capability: String? = nil,
        payload: JSONValue
    ) {
        self.type = type
        self.id = id
        self.replyTo = replyTo
        self.sessionId = sessionId
        self.sentAt = sentAt ?? Self.makeTimestamp()
        self.capability = capability ?? "tool.exec"
        self.payload = payload
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        type = try container.decode(String.self, forKey: .type)
        id = try container.decode(String.self, forKey: .id)
        replyTo = try container.decodeIfPresent(String.self, forKey: .replyTo)
        sessionId = try container.decodeIfPresent(String.self, forKey: .sessionId)
        sentAt = try container.decodeIfPresent(String.self, forKey: .sentAt) ?? Self.makeTimestamp()
        capability = try container.decodeIfPresent(String.self, forKey: .capability) ?? "tool.exec"
        payload = try container.decodeIfPresent(JSONValue.self, forKey: .payload) ?? .object([:])
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(type, forKey: .type)
        try container.encode(id, forKey: .id)
        try container.encodeIfPresent(replyTo, forKey: .replyTo)
        try container.encodeIfPresent(sessionId, forKey: .sessionId)
        try container.encode(sentAt, forKey: .sentAt)
        try container.encodeIfPresent(capability, forKey: .capability)
        try container.encode(payload, forKey: .payload)
    }

    public static func makeTimestamp() -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: Date())
    }
}
