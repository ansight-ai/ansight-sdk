import Foundation
import Network

public struct ConnectRequest: Sendable, Codable, Equatable {
    public let type: String
    public let ver: Int
    public let requestId: String
    public let enrollmentMode: String
    public let inviteId: String
    public let appId: String
    public let deviceId: String
    public let deviceName: String
    public let accessToken: String
    public let processSessionId: String?

    public init(
        type: String = "ENROLLMENT_CONNECT",
        ver: Int = 2,
        requestId: String,
        enrollmentMode: String = "invite",
        inviteId: String,
        appId: String,
        deviceId: String,
        deviceName: String,
        accessToken: String,
        processSessionId: String? = ProcessSessionIdentity.current
    ) {
        self.type = type
        self.ver = ver
        self.requestId = requestId
        self.enrollmentMode = enrollmentMode
        self.inviteId = inviteId
        self.appId = appId
        self.deviceId = deviceId
        self.deviceName = deviceName
        self.accessToken = accessToken
        self.processSessionId = processSessionId
    }
}
