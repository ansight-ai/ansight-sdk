import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

public struct DeviceRuntimeProfile: Sendable, Codable, Equatable {
    public var primary: Int?
    public var primaryVersion: String?
    public var engine: DeviceRuntimeEngineProfile?
    public var stack: [DeviceRuntimeStackEntry]?
    public var aotEnabled: Bool?
    public var jitEnabled: Bool?
}
