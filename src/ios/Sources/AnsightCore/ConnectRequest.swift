import Foundation
import Network

public struct ConnectRequest: Sendable, Codable, Equatable {
    public let type: String
    public let ver: Int
    public let configId: String
    public let oneTimeToken: String
    public let appId: String
    public let clientName: String
    public let processSessionId: String?

    public init(
        type: String = "CONNECT_REQ",
        ver: Int = 1,
        configId: String,
        oneTimeToken: String,
        appId: String,
        clientName: String,
        processSessionId: String? = ProcessSessionIdentity.current
    ) {
        self.type = type
        self.ver = ver
        self.configId = configId
        self.oneTimeToken = oneTimeToken
        self.appId = appId
        self.clientName = clientName
        self.processSessionId = processSessionId
    }
}
