import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceNetworkProfile: Sendable, Codable, Equatable {
    public var transportCode: Int?
    public var metered: Bool?
    public var effectiveType: String?
    public var rttMs: Int?
    public var downKbps: Int?
}
