import Foundation
import Network

public struct PairingConnectionOptions: Sendable, Equatable {
    public var hostAddressOverride: String?
    public var discoveryPort: Int?
    public var deviceAppProfile: DeviceAppProfile?
    public var customProperties: [String: [String: String]]
    public var requestedScopes: [String]
    public var requestCritical: Bool
    public var allowInsecureV1: Bool

    public init(
        hostAddressOverride: String? = nil,
        discoveryPort: Int? = nil,
        deviceAppProfile: DeviceAppProfile? = nil,
        customProperties: [String: [String: String]] = [:],
        requestedScopes: [String] = [],
        requestCritical: Bool = false,
        allowInsecureV1: Bool = false
    ) {
        self.hostAddressOverride = hostAddressOverride
        self.discoveryPort = discoveryPort
        self.deviceAppProfile = deviceAppProfile
        self.customProperties = customProperties
        self.requestedScopes = requestedScopes
        self.requestCritical = requestCritical
        self.allowInsecureV1 = allowInsecureV1
    }
}
