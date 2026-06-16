import Foundation
import Network

public struct PairingControlEnvelope: Sendable, Codable, Equatable {
    public static let requestType = "CONTROL_REQ"
    public static let responseType = "CONTROL_RESP"

    public let type: String
    public let id: String?
    public let replyTo: String?
    public let action: String
    public let payload: JSONValue?
    public let success: Bool
    public let message: String?

    public init(
        type: String,
        id: String? = nil,
        replyTo: String? = nil,
        action: String,
        payload: JSONValue? = nil,
        success: Bool = true,
        message: String? = nil
    ) {
        self.type = type
        self.id = id
        self.replyTo = replyTo
        self.action = action
        self.payload = payload
        self.success = success
        self.message = message
    }
}
