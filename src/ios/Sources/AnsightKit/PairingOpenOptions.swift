import Foundation

public struct PairingOpenOptions: Sendable {
    public var clientName: String
    public var expectedAppId: String?
    public var hostAddressOverride: String?
    public var discoveryPort: Int?
    public var profileOverride: [String: String]

    public init(
        clientName: String,
        expectedAppId: String? = nil,
        hostAddressOverride: String? = nil,
        discoveryPort: Int? = nil,
        profileOverride: [String: String] = [:]
    ) {
        self.clientName = clientName
        self.expectedAppId = expectedAppId
        self.hostAddressOverride = hostAddressOverride
        self.discoveryPort = discoveryPort
        self.profileOverride = profileOverride
    }
}
