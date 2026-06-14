import Foundation
import Network

public struct ConnectResponse: Sendable, Codable, Equatable {
    public let type: String
    public let ver: Int
    public let accepted: Bool
    public let reason: String
    public let reasonMessage: String?
    public let hostId: String
    public let hostName: String
    public let hostWifiName: String?
    public let message: String
    public let webSocketPort: Int?
    public let webSocketPath: String?
    public let webSocketToken: String?
}
