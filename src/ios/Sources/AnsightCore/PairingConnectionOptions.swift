import Foundation
import Network

public struct PairingConnectionOptions: Sendable, Equatable {
    public var hostAddressOverride: String?
    public var discoveryPort: Int?
    public var allowCellularConnections: Bool
    public var deviceAppProfile: DeviceAppProfile?
    public var customProperties: [String: [String: String]]

    public init(
        hostAddressOverride: String? = nil,
        discoveryPort: Int? = nil,
        allowCellularConnections: Bool = false,
        deviceAppProfile: DeviceAppProfile? = nil,
        customProperties: [String: [String: String]] = [:]
    ) {
        self.hostAddressOverride = hostAddressOverride
        self.discoveryPort = discoveryPort
        self.allowCellularConnections = allowCellularConnections
        self.deviceAppProfile = deviceAppProfile
        self.customProperties = customProperties
    }
}
